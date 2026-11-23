// Figé à la compilation (contrairement à appsettings.json côté .NET) : changer l'URL de l'API demande de recompiler.
// Chaîne vide = chemin relatif « /api/... » : en local, ng serve (proxy.conf.mjs) ou nginx relaient vers l'API (ADR 0017).
// En S3, un environment.azure.ts (fileReplacements dans angular.json) portera l'URL d'App Service.
export const environment = {
  apiBaseUrl: '',
};
