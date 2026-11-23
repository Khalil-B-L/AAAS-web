// Relais du serveur de développement (ng serve) : le navigateur appelle http://localhost:4200/api/...,
// le serveur de développement transmet à l'API. Même origine vue du navigateur : pas de CORS (ADR 0017).
// API lancée par « dotnet run » : http://localhost:5080 (défaut).
// API lancée dans docker compose : API_URL=http://localhost:8080 npm start
const target = process.env['API_URL'] ?? 'http://localhost:5080';

export default {
  '/api': {
    target,
    secure: false,
  },
};
