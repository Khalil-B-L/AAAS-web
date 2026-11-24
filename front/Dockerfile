# Image du front Angular (ADR 0017). Contexte de build = dossier front/ :
#   docker build -t aaas-front:local front
# Deux étapes : Node (lourd) pour COMPILER, nginx (léger) pour SERVIR les fichiers produits.
# Même idée que l'API : SDK .NET pour publier, runtime ASP.NET pour exécuter.

# ---------- Étape 1 : compilation ----------
# Tag précis (version de Node + version d'Alpine) : un build d'aujourd'hui et de dans six mois utilisent la même image.
FROM node:24.22.3-alpine3.24 AS build
WORKDIR /app

# Fichiers de dépendances d'abord : tant qu'ils ne changent pas, la couche « npm ci » vient du cache
# (modifier un composant ne relance pas le téléchargement des paquets).
COPY package.json package-lock.json ./
# npm ci : installe exactement package-lock.json, dans un node_modules vide (reproductible), échoue s'il
# n'est pas cohérent avec package.json. Jamais « npm install » dans une image.
RUN npm ci --no-audit --no-fund

# Le code ensuite (front/.dockerignore exclut node_modules, dist, .angular…).
COPY . .
RUN npx ng build --configuration production

# ---------- Étape 2 : image finale ----------
# nginx « unprivileged » : processus non-root (utilisateur nginx, UID 101), à l'écoute sur 8080
# (un port < 1024 exigerait root). Même principe que « USER $APP_UID » dans l'image de l'API.
FROM nginxinc/nginx-unprivileged:1.31.7-alpine3.24 AS final

# Configuration : repli SPA, relais /api vers le conteneur api, cache, gzip, en-têtes de sécurité, /healthz.
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf

# On ne récupère QUE le résultat de la compilation : ni Node, ni sources, ni node_modules dans l'image finale.
COPY --from=build /app/dist/front/browser /usr/share/nginx/html

EXPOSE 8080
# ENTRYPOINT et CMD (nginx au premier plan, PID 1) sont hérités de l'image de base.
