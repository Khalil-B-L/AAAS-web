// Parcours navigateur réel : Edge ou Chrome sans interface, piloté par le protocole DevTools (CDP), sans dépendance npm.
// Ce n'est pas une suite e2e outillée (Playwright, question Q-T1) : un contrôle rapide des parcours du plan front § 21.
// Prérequis et procédure : docs/technique/exploitation.md, « Parcours navigateur ».
// Usage : node e2e/parcours-navigateur.mjs  (variables ci-dessous)
const port = process.env.E2E_CDP_PORT ?? '9333';                       // --remote-debugging-port du navigateur
const BASE = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:4300';      // front (conteneur nginx ou ng serve)
const adminEmail = process.env.E2E_ADMIN_EMAIL;                         // compte admin existant (Bootstrap)
const adminPassword = process.env.E2E_ADMIN_PASSWORD;
const eventId = process.env.E2E_EVENT_ID;                               // événement publié, 10:00-12:30 heure de Tunis
const eventDate = process.env.E2E_EVENT_DATE;                           // son jour, AAAA-MM-JJ (J+30, calculé par pile-e2e.sh)
if (!adminEmail || !adminPassword || !eventId || !/^\d{4}-\d{2}-\d{2}$/.test(eventDate ?? '')) {
  console.error('Définir E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD, E2E_EVENT_ID et E2E_EVENT_DATE (AAAA-MM-JJ).');
  process.exit(2);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let finished = false;
ws.addEventListener('close', () => {
  if (!finished) {
    console.error('ÉCHEC connexion CDP fermée par le navigateur en cours de parcours (profil déjà utilisé ?)');
    process.exit(1);
  }
});

let nextId = 1;
const pending = new Map();
const problems = [];
ws.addEventListener('message', (event) => {
  const msg = JSON.parse(event.data);
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  } else if (msg.method === 'Log.entryAdded' && ['error', 'warning'].includes(msg.params.entry.level)) {
    problems.push(`${msg.params.entry.level}: ${msg.params.entry.text}`);
  } else if (msg.method === 'Runtime.exceptionThrown') {
    problems.push(`exception: ${msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text}`);
  } else if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') {
    problems.push(`console.error: ${msg.params.args.map((a) => a.value ?? a.description).join(' ')}`);
  }
});
const send = (method, params = {}) =>
  new Promise((resolve) => {
    const id = nextId++;
    pending.set(id, resolve);
    ws.send(JSON.stringify({ id, method, params }));
  });
const evaluate = async (expression) =>
  (await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result?.result?.value;
const goto = async (path) => {
  await send('Page.navigate', { url: BASE + path });
  await sleep(2500);
};
const bodyText = () => evaluate(`document.querySelector('main').innerText.replace(/\\s+/g, ' ').slice(0, 600)`);
const check = (label, condition, detail = '') => console.log(`${condition ? 'OK  ' : 'ÉCHEC'} ${label}${detail ? ' — ' + detail : ''}`);

await send('Page.enable');
await send('Runtime.enable');
await send('Log.enable');
await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });

// 1. Connexion admin par le formulaire (événements input + submit, comme un utilisateur).
await goto('/connexion?returnUrl=%2Fadmin%2Fevenements');
await evaluate(`(() => {
  const set = (sel, v) => { const i = document.querySelector(sel); i.value = v; i.dispatchEvent(new Event('input', { bubbles: true })); };
  set('input[formControlName=email]', ${JSON.stringify(adminEmail)});
  set('input[formControlName=password]', ${JSON.stringify(adminPassword)});
  document.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
})()`);
await sleep(2500);
const url1 = await evaluate('location.pathname');
const header = await evaluate(`document.querySelector('header').innerText.replace(/\\s+/g, ' ')`);
check('connexion admin puis retour à returnUrl', url1 === '/admin/evenements', url1);
check('en-tête admin (prénom + Administration)', /Bonjour Admin/.test(header) && /Administration/.test(header), header);
const stored = await evaluate(`Object.keys(JSON.parse(localStorage.getItem('aaas.session')))`);
check('localStorage = { accessToken, localDeadline } uniquement', JSON.stringify(stored) === '["accessToken","localDeadline"]', JSON.stringify(stored));

// 2. Liste admin.
const list = await bodyText();
check('liste admin : événement, statut, places', /Atelier couture/.test(list) && /Publié/.test(list), list.slice(0, 250));

// 3. Fiche admin : valeurs à l'heure de Tunis.
await goto(`/admin/evenements/${eventId}`);
const values = await evaluate(`[...document.querySelectorAll('input[formControlName]')].map(i => i.getAttribute('formControlName') + '=' + i.value).join(' | ')`);
check(
  'fiche admin préremplie à l’heure de Tunis (10:00, 12:30)',
  values.includes(`startsAt=${eventDate}T10:00`) && values.includes(`endsAt=${eventDate}T12:30`),
  values,
);

// 4. Modification : le bouton Dépublier se grise dès la saisie (UX-7), puis enregistrement.
await evaluate(`(() => { const i = document.querySelector('input[formControlName=location]'); i.value = 'Local de l\\'association, Tunis centre'; i.dispatchEvent(new Event('input', { bubbles: true })); })()`);
await sleep(300);
const unpublishDisabled = await evaluate(`[...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Dépublier')?.disabled`);
check('Dépublier désactivé tant que le formulaire est modifié', unpublishDisabled === true);
await evaluate(`document.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))`);
await sleep(2000);
const toast = await evaluate(`document.querySelector('app-notification-host').innerText`);
check('enregistrement : message « Enregistré. »', /Enregistré\./.test(toast ?? ''), toast);

// 5. Numéro de contact.
await goto('/admin/parametres');
const settings = await bodyText();
check('page numéro de contact (état vide)', /Aucun numéro n'est défini pour le moment/.test(settings), settings.slice(0, 200));

// 6. Rechargement d'une route profonde (repli SPA nginx) avec session restaurée sans appel /me.
await goto(`/admin/evenements/${eventId}`);
check('F5 sur une route profonde admin', (await evaluate('location.pathname')) === `/admin/evenements/${eventId}`);

// 7. Déconnexion.
await evaluate(`[...document.querySelectorAll('header button')].find(b => b.textContent.trim() === 'Se déconnecter').click()`);
await sleep(1500);
check('déconnexion : retour aux événements, session effacée',
  (await evaluate('location.pathname')) === '/evenements' && (await evaluate(`localStorage.getItem('aaas.session')`)) === null);

// 8. Téléphone : menu repliable.
await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
await goto('/evenements');
const navHidden = await evaluate(`getComputedStyle(document.querySelector('#menu-principal')).display`);
check('mobile : menu replié derrière « Menu »', navHidden === 'none', navHidden);

const csp = problems.filter((p) => /Content Security Policy|Refused/i.test(p));
check('aucune violation CSP dans la console', csp.length === 0, csp.join(' / '));
console.log('Autres messages console :', problems.filter((p) => !csp.includes(p)));
finished = true;
ws.close();

// Ferme uniquement l'instance Edge lancée pour ce test (et pas le navigateur de l'utilisateur).
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = new WebSocket(version.webSocketDebuggerUrl);
await new Promise((r) => browser.addEventListener('open', r, { once: true }));
browser.send(JSON.stringify({ id: 1, method: 'Browser.close' }));
await sleep(500);
process.exit(0);
