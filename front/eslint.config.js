// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
      // XSS (ADR 0023) : aucune insertion de HTML brut ni contournement du DomSanitizer dans le code TypeScript.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'MemberExpression[property.name=/^(innerHTML|outerHTML)$/]',
          message: 'innerHTML / outerHTML interdits (XSS, ADR 0023) : afficher le texte par interpolation {{ }}.',
        },
        {
          selector: 'CallExpression[callee.property.name=/^(insertAdjacentHTML|write|writeln)$/]',
          message: 'Insertion de HTML brut interdite (XSS, ADR 0023).',
        },
        {
          selector: 'MemberExpression[property.name=/^bypassSecurityTrust/]',
          message: 'bypassSecurityTrust* désactive le DomSanitizer : interdit (XSS, ADR 0023).',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // Même règle dans les templates (fichiers .html et templates en ligne) : [innerHTML] / [outerHTML] interdits.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'BoundAttribute[name=/^(innerHTML|outerHTML|srcdoc)$/i]',
          message: '[innerHTML] interdit (XSS, ADR 0023) : afficher le texte par interpolation {{ }}.',
        },
      ],
    },
  },
]);
