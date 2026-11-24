#!/usr/bin/env bash
# Pile JETABLE pour vérifier le front de bout en bout, sans toucher à la pile « docker compose » locale :
# réseau, base, API et front séparés (préfixe aaas-e2e), front publié sur http://127.0.0.1:4300.
#
#   bash front/e2e/pile-e2e.sh up        construit les images, démarre, crée les données de test
#   bash front/e2e/pile-e2e.sh parcours  lance le parcours navigateur (Edge ou Chrome sans interface)
#   bash front/e2e/pile-e2e.sh reservations  parcours HTTP des réservations (demande, 409, « ma demande », annulation)
#   bash front/e2e/pile-e2e.sh news      parcours HTTP des actualités (créer, publier, lire, 409, dépublier, supprimer)
#   bash front/e2e/pile-e2e.sh down      supprime tout (conteneurs, réseau, images aaas-api:e2e et aaas-front:e2e)
#
# À lancer depuis la racine du dépôt, sous Git Bash ou Linux. Détails : docs/technique/exploitation.md.
set -euo pipefail

STATE_DIR="${TMPDIR:-/tmp}/aaas-e2e"
BASE="http://127.0.0.1:4300"
ADMIN_EMAIL="admin-e2e@exemple.test"
SQL_IMAGE="mcr.microsoft.com/mssql/server:2022-CU27-GDR2-ubuntu-22.04"
mkdir -p "$STATE_DIR"

random() { node -e "console.log(require('crypto').randomBytes(${1:-16}).toString('hex'))"; }

# Lit un champ de premier niveau d'un JSON reçu sur l'entrée standard. Échoue (code 1) si le JSON est illisible
# ou si le champ est absent : sans cela, « event_id » vide ferait échouer plus loin, avec un message trompeur.
# Accès par o[nom] : jamais d'eval sur un argument.
json_field() {
  node -e "
    let s = '';
    process.stdin.on('data', (d) => (s += d)).on('end', () => {
      const name = process.argv[1];
      let o;
      try { o = JSON.parse(s); } catch { console.error('Réponse non JSON : ' + s.slice(0, 200)); process.exit(1); }
      if (o === null || typeof o !== 'object' || !Object.hasOwn(o, name) || o[name] === null || o[name] === '') {
        console.error('Champ « ' + name + ' » absent de : ' + s.slice(0, 200));
        process.exit(1);
      }
      console.log(o[name]);
    });" "$1"
}

# Attend qu'une commande réussisse, en la relançant toutes les 2 s ; ÉCHOUE au bout du délai (en secondes).
#   wait_until 120 "SQL Server" docker exec ...
wait_until() {
  local timeout="$1" label="$2"
  shift 2
  local deadline=$((SECONDS + timeout))
  until "$@" >/dev/null 2>&1; do
    if ((SECONDS >= deadline)); then
      echo "ÉCHEC : $label pas prêt après ${timeout} s. Diagnostic : docker logs aaas-e2e-sql / aaas-e2e-api / aaas-e2e-front" >&2
      return 1
    fi
    sleep 2
  done
  echo "    $label prêt ($((SECONDS - deadline + timeout)) s)"
}

# Date de l'événement de test : J+30 à l'heure de Tunis, au format AAAA-MM-JJ (« sv-SE » produit ce format).
# Une date codée en dur finit par passer dans le passé, et l'API refuse alors la création (date dans le futur).
event_date() {
  node -e "console.log(new Date(Date.now() + 30 * 86400000).toLocaleDateString('sv-SE', { timeZone: 'Africa/Tunis' }))"
}

up() {
  local sa admin_password jwt_key
  sa="E2e-$(random 12)Aa1"
  admin_password="Adm1n$(random 6)"
  jwt_key="$(random 48)"
  # Fichier créé directement en 600 (umask dans un sous-shell) : le mot de passe n'est jamais lisible par d'autres.
  (umask 077 && echo "$admin_password" > "$STATE_DIR/admin-password")
  chmod 600 "$STATE_DIR/admin-password"

  # Tags :e2e dédiés : l'image aaas-front:local de la pile « docker compose » n'est ni écrasée ni supprimée.
  echo "1/5 Images (tags aaas-api:e2e et aaas-front:e2e)"
  docker build -q -f src/Aaas.Api/Dockerfile -t aaas-api:e2e . >/dev/null
  docker build -q -t aaas-front:e2e front >/dev/null

  echo "2/5 Réseau et SQL Server (aucun port publié)"
  docker network create aaas-e2e >/dev/null
  docker run -d --name aaas-e2e-sql --network aaas-e2e -e ACCEPT_EULA=Y -e MSSQL_PID=Developer \
    -e "MSSQL_SA_PASSWORD=$sa" "$SQL_IMAGE" >/dev/null
  # Même sonde que le healthcheck de docker-compose.yml : SQL Server accepte réellement une requête.
  # MSYS_NO_PATHCONV : sous Git Bash, /opt/... serait sinon converti en chemin Windows avant d'atteindre docker.
  wait_until 120 "SQL Server" env MSYS_NO_PATHCONV=1 docker exec aaas-e2e-sql /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$sa" -C -b -Q "SELECT 1"

  echo "3/5 API (alias réseau « api », le nom attendu par nginx)"
  docker run -d --name aaas-e2e-api --network aaas-e2e --network-alias api \
    -e ASPNETCORE_ENVIRONMENT=Development \
    -e "ConnectionStrings__Default=Server=aaas-e2e-sql,1433;Database=Aaas;User Id=sa;Password=$sa;Encrypt=True;TrustServerCertificate=True" \
    -e "Jwt__SigningKey=$jwt_key" -e Database__MigrateOnStartup=true \
    -e "Bootstrap__AdminEmail=$ADMIN_EMAIL" -e "Bootstrap__AdminPassword=$admin_password" \
    -e Bootstrap__AdminFirstName=Admin -e Bootstrap__AdminLastName=E2E -e "Bootstrap__AdminPhone=+216 20 000 000" \
    aaas-api:e2e >/dev/null

  echo "4/5 Front sur $BASE"
  docker run -d --name aaas-e2e-front --network aaas-e2e -p 127.0.0.1:4300:8080 aaas-front:e2e >/dev/null
  # nginx → API → base : 200 seulement quand toute la chaîne répond (migration appliquée comprise).
  wait_until 180 "front + API" curl -fsS -o /dev/null "$BASE/api/events"

  local day
  day="$(event_date)"
  echo "5/5 Données : un événement publié le $day de 10:00 à 12:30 (heure de Tunis)"
  local token event_id
  # -f : code retour non nul sur un statut HTTP ≥ 400 (set -e arrête alors le script) ; -sS : silencieux, sauf les erreurs.
  token=$(curl -fsS -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$admin_password\"}" | json_field accessToken)
  event_id=$(curl -fsS -X POST "$BASE/api/admin/events" -H 'Content-Type: application/json' -H "Authorization: Bearer $token" \
    -d "{\"title\":\"Atelier couture\",\"description\":\"Initiation.\",\"location\":\"Tunis\",\"startsAt\":\"${day}T10:00+01:00\",\"endsAt\":\"${day}T12:30+01:00\",\"capacity\":30,\"price\":15}" \
    | json_field id)
  curl -fsS -o /dev/null -X POST "$BASE/api/admin/events/$event_id/publish" -H "Authorization: Bearer $token"
  echo "$event_id" > "$STATE_DIR/event-id"
  echo "$day" > "$STATE_DIR/event-date"
  echo "Prêt : $BASE (admin $ADMIN_EMAIL, mot de passe dans $STATE_DIR/admin-password)"
}

parcours() {
  local browser profile port
  browser="${E2E_BROWSER:-/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe}"
  # Profil neuf ET port aléatoire à chaque passage : sinon le script peut se connecter à une instance
  # précédente encore ouverte, dont le localStorage contient une session d'une autre pile (jeton refusé, 401).
  profile="$STATE_DIR/profil-$(random 4)"
  port=$((9300 + RANDOM % 600))
  "$browser" --headless=new --disable-gpu --no-first-run --remote-debugging-port="$port" \
    --user-data-dir="$profile" about:blank >/dev/null 2>&1 &
  wait_until 30 "navigateur (CDP sur le port $port)" curl -fsS -o /dev/null "http://127.0.0.1:$port/json/version"
  E2E_CDP_PORT="$port" E2E_ADMIN_EMAIL="$ADMIN_EMAIL" E2E_ADMIN_PASSWORD="$(cat "$STATE_DIR/admin-password")" \
    E2E_EVENT_ID="$(cat "$STATE_DIR/event-id")" E2E_EVENT_DATE="$(cat "$STATE_DIR/event-date")" \
    node front/e2e/parcours-navigateur.mjs
}

# Code HTTP seul (corps dans $STATE_DIR/last-body) : on vérifie les 409 sans que -f n'arrête le script.
http_status() {
  curl -sS -o "$STATE_DIR/last-body" -D "$STATE_DIR/last-headers" -w "%{http_code}" "$@"
}

expect_status() {
  local expected="$1" label="$2"
  shift 2
  local status
  status=$(http_status "$@")
  if [[ "$status" != "$expected" ]]; then
    echo "ÉCHEC : $label → $status (attendu $expected) : $(head -c 300 "$STATE_DIR/last-body")" >&2
    return 1
  fi
  echo "    OK $label → $status"
}

# Tâche A des réservations (US-D1 à D4), à travers nginx comme le navigateur.
reservations() {
  local event_id email token reservation_id
  event_id="$(cat "$STATE_DIR/event-id")"
  email="membre-$(random 4)@exemple.test"
  token=$(curl -fsS -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json'     -d "{\"firstName\":\"Membre\",\"lastName\":\"E2E\",\"email\":\"$email\",\"phoneNumber\":\"+216 20 111 222\",\"password\":\"motdepasse1\",\"acceptPrivacyPolicy\":true}"     | json_field accessToken)
  local auth=(-H "Authorization: Bearer $token")
  local json=(-H 'Content-Type: application/json')
  # Rejouable : on compare aux places lues AVANT (un passage précédent ou le parcours navigateur a pu en prendre).
  local seats_before
  seats_before=$(curl -fsS "$BASE/api/events/$event_id" | json_field remainingSeats)

  expect_status 401 "visiteur sur /api/me/reservations" "$BASE/api/me/reservations"
  expect_status 204 "aucune demande pour cet événement" "${auth[@]}" "$BASE/api/me/events/$event_id/reservation"
  grep -qi '^cache-control: no-store' "$STATE_DIR/last-headers" || { echo "ÉCHEC : Cache-Control no-store absent" >&2; return 1; }
  echo "    OK Cache-Control: no-store"
  expect_status 201 "demande de place" -X POST "${auth[@]}" "${json[@]}" -d "{\"eventId\":\"$event_id\"}" "$BASE/api/me/reservations"
  reservation_id=$(json_field id < "$STATE_DIR/last-body")
  expect_status 409 "deuxième demande (already_reserved)" -X POST "${auth[@]}" "${json[@]}" -d "{\"eventId\":\"$event_id\"}" "$BASE/api/me/reservations"
  [[ "$(json_field reservationId < "$STATE_DIR/last-body")" == "$reservation_id" ]] || { echo "ÉCHEC : reservationId" >&2; return 1; }
  expect_status 200 "ma demande sur le détail" "${auth[@]}" "$BASE/api/me/events/$event_id/reservation"
  local seats_after
  seats_after=$(curl -fsS "$BASE/api/events/$event_id" | json_field remainingSeats)
  [[ "$seats_after" == "$((seats_before - 1))" ]] || { echo "ÉCHEC : places restantes $seats_after (attendu $((seats_before - 1)))" >&2; return 1; }
  echo "    OK places restantes : $seats_before → $seats_after"
  expect_status 200 "mes réservations (à venir)" "${auth[@]}" "$BASE/api/me/reservations?scope=current"
  expect_status 200 "annulation" -X POST "${auth[@]}" "$BASE/api/me/reservations/$reservation_id/cancel"
  expect_status 409 "deuxième annulation (reservation_state_changed)" -X POST "${auth[@]}" "$BASE/api/me/reservations/$reservation_id/cancel"
  expect_status 201 "nouvelle demande après annulation" -X POST "${auth[@]}" "${json[@]}" -d "{\"eventId\":\"$event_id\"}" "$BASE/api/me/reservations"
  # Rend la place : le parcours suivant (ou un nouveau passage) retrouve le même nombre de places.
  expect_status 200 "annulation de fin de parcours" -X POST "${auth[@]}" "$BASE/api/me/reservations/$(json_field id < "$STATE_DIR/last-body")/cancel"
  [[ "$(curl -fsS "$BASE/api/events/$event_id" | json_field remainingSeats)" == "$seats_before" ]] || { echo "ÉCHEC : place non rendue" >&2; return 1; }
  echo "    OK places revenues à $seats_before"
  echo "Parcours réservations : OK"
}

# Actualités (US-E1, US-B1, US-B2), à travers nginx : créer, brouillon invisible, publier, lire anonymement,
# texte malveillant renvoyé à l'identique, 409, dépublier → 404, supprimer. Rejouable (titre aléatoire, suppression en fin).
news() {
  local token news_id title
  token=$(curl -fsS -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$(cat "$STATE_DIR/admin-password")\"}" | json_field accessToken)
  local auth=(-H "Authorization: Bearer $token")
  local json=(-H 'Content-Type: application/json')
  title="AG e2e $(random 3)"

  expect_status 401 "visiteur sur /api/admin/news" "$BASE/api/admin/news"
  # Le texte contient <script> et <img onerror> : l'API doit le renvoyer tel quel (le front l'affiche en texte).
  expect_status 201 "création (brouillon)" -X POST "${auth[@]}" "${json[@]}" \
    -d "{\"title\":\"$title\",\"content\":\"Premier paragraphe.\\n\\n<script>alert(1)</script><img src=x onerror=alert(1)>\"}" \
    "$BASE/api/admin/news"
  news_id=$(json_field id < "$STATE_DIR/last-body")
  grep -qi '^cache-control: no-store' "$STATE_DIR/last-headers" || { echo "ÉCHEC : Cache-Control no-store absent" >&2; return 1; }
  echo "    OK Cache-Control: no-store"
  expect_status 404 "brouillon invisible du public" "$BASE/api/news/$news_id"
  expect_status 404 "identifiant mal formé (même 404, anonyme)" "$BASE/api/news/abc"
  expect_status 200 "publication" -X POST "${auth[@]}" "$BASE/api/admin/news/$news_id/publish"
  expect_status 409 "deuxième publication (news_state_changed)" -X POST "${auth[@]}" "$BASE/api/admin/news/$news_id/publish"
  expect_status 200 "liste publique" "$BASE/api/news"
  grep -q "$title" "$STATE_DIR/last-body" || { echo "ÉCHEC : actualité absente de la liste publique" >&2; return 1; }
  echo "    OK présente dans la liste publique"
  expect_status 200 "détail public" "$BASE/api/news/$news_id"
  grep -q '<script>alert(1)</script><img src=x onerror=alert(1)>' "$STATE_DIR/last-body" \
    || { echo "ÉCHEC : texte modifié par le serveur" >&2; return 1; }
  echo "    OK texte renvoyé à l'identique (aucun encodage serveur)"
  expect_status 409 "modification avec une version périmée (concurrent_update)" -X PUT "${auth[@]}" "${json[@]}" \
    -d "{\"title\":\"$title\",\"content\":\"x\",\"version\":\"AAAAAAAAAAA=\"}" "$BASE/api/admin/news/$news_id"
  expect_status 200 "modification avec la bonne version" -X PUT "${auth[@]}" "${json[@]}" \
    -d "{\"title\":\"$title\",\"content\":\"Texte corrige (ASCII : curl sous Git Bash n envoie pas l UTF-8 des arguments).\",\"version\":\"$(curl -fsS "${auth[@]}" "$BASE/api/admin/news/$news_id" | json_field version)\"}" \
    "$BASE/api/admin/news/$news_id"
  expect_status 200 "dépublication" -X POST "${auth[@]}" "$BASE/api/admin/news/$news_id/unpublish"
  expect_status 404 "dépubliée → 404 public" "$BASE/api/news/$news_id"
  expect_status 204 "suppression" -X DELETE "${auth[@]}" "$BASE/api/admin/news/$news_id"
  expect_status 404 "deuxième suppression" -X DELETE "${auth[@]}" "$BASE/api/admin/news/$news_id"
  expect_status 200 "accueil SPA (/) servi par nginx" "$BASE/"
  echo "Parcours actualités : OK"
}

down() {
  docker rm -f aaas-e2e-front aaas-e2e-api aaas-e2e-sql >/dev/null 2>&1 || true
  docker network rm aaas-e2e >/dev/null 2>&1 || true
  docker image rm aaas-api:e2e aaas-front:e2e >/dev/null 2>&1 || true
  # Le navigateur met quelques secondes à libérer son profil après Browser.close.
  sleep 3
  rm -rf "$STATE_DIR" 2>/dev/null || echo "Profil navigateur encore verrouillé : supprimer $STATE_DIR plus tard."
  echo "Pile e2e supprimée."
}

# Liste blanche : l'argument n'est jamais exécuté tel quel (« pile-e2e.sh rm » ou une faute de frappe échouent proprement).
case "${1:-}" in
  up | parcours | reservations | news | down) "$1" ;;
  *)
    echo "Usage : pile-e2e.sh up|parcours|reservations|news|down" >&2
    exit 2
    ;;
esac
