module.exports = {
  root: true,
  ignorePatterns: [
    "**/dist/**",
    "**/node_modules/**",
    "BugenceEditConsole/wwwroot/**",
    "BugenceEditConsole/bin/**",
    "BugenceEditConsole/obj/**",
    "_framework/**",
    "temp_zip_extract/**",
    "*.d.ts",
    "**/*.js"
  ],
  parser: "@typescript-eslint/parser",
  parserOptions: {
    project: [
      "./packages/core/tsconfig.json",
      "./packages/dashboard/tsconfig.json",
      "./packages/canvas/tsconfig.json",
      "./packages/ui/tsconfig.json",
      "./packages/analytics/tsconfig.json"
    ],
    tsconfigRootDir: __dirname,
    sourceType: "module"
  },
  plugins: ["@typescript-eslint", "import"],
  extends: [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended",
    "plugin:@typescript-eslint/recommended-requiring-type-checking",
    "plugin:import/recommended",
    "plugin:import/typescript",
    "prettier"
  ],
  env: {
    es2021: true,
    browser: true,
    node: true
  },
  settings: {
    "import/resolver": {
      typescript: {
        project: [
          "./packages/core/tsconfig.json",
          "./packages/dashboard/tsconfig.json",
          "./packages/canvas/tsconfig.json",
          "./packages/ui/tsconfig.json",
          "./packages/analytics/tsconfig.json"
        ]
      }
    }
  },
  rules: {
    "import/no-unresolved": "off",
    "import/order": [
      "warn",
      {
        groups: [
          "builtin",
          "external",
          "internal",
          "parent",
          "sibling",
          "index",
          "object",
          "type"
        ],
        "newlines-between": "always",
        alphabetize: {
          order: "asc",
          caseInsensitive: true
        }
      }
    ],
    "@typescript-eslint/explicit-module-boundary-types": "off",
    "@typescript-eslint/no-floating-promises": [
      "error",
      {
        ignoreVoid: true
      }
    ],
    "@typescript-eslint/no-misused-promises": [
      "error",
      {
        checksVoidReturn: false
      }
    ]
  },
  overrides: [
    {
      files: ["**/*.cjs", "**/*.mjs", "**/*.js"],
      parser: "@typescript-eslint/parser",
      parserOptions: {
        project: null
      },
      rules: {
        "@typescript-eslint/no-var-requires": "off"
      }
    },
    {
      files: ["**/*.test.ts", "**/*.spec.ts"],
      env: {
        node: true
      }
    }
  ]
};
