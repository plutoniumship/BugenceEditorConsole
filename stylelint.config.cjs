module.exports = {
  extends: ["stylelint-config-standard", "stylelint-config-standard-scss", "stylelint-config-prettier"],
  plugins: ["stylelint-order"],
  rules: {
    "order/properties-alphabetical-order": true,
    "selector-class-pattern": null,
    "selector-id-pattern": null,
    "custom-property-pattern": null,
    "no-descending-specificity": null
  },
  overrides: [
    {
      files: ["**/*.scss"],
      customSyntax: "postcss-scss"
    }
  ],
  ignoreFiles: [
    "**/dist/**",
    "node_modules/**",
    "BugenceEditConsole/wwwroot/**",
    "_framework/**",
    "temp_zip_extract/**"
  ]
};
