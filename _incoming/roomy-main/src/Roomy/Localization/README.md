# Localisation

Every user-facing string lives in `strings.json`: one entry per key with the
English text (`en`, required) and one field per language (`ru`, `zh-Hans`).
The generated files come out of it:

- `S.g.cs` — typed accessors (`S.ScanMyPc`), used from code and XAML.
- `Strings.resx`, `Strings.ru.resx`, `Strings.zh-Hans.resx` — the catalogues the app loads at runtime.

A string without a translation uses the English text, and the generator prints a
warning listing it, so new text can land before every language catches up.

Regenerate them after editing `strings.json`:

```bash
node tools/strings.mjs
```

## Using a string

```csharp
// plain string
Title = S.CatRecycle;

// template with placeholders (order is whatever the translation needs)
Detail = Loc.Format("DownloadedAgo", Format.Ago(t));

// plurals: Russian gets one/few/many, English uses one/many
Detail = $"{count:N0} {Loc.Items(count)}";
```

```xml
<TextBlock Text="{x:Static loc:S.ScanMyPc}" />
```

## Adding a language

1. Add a field for the language to every entry in `strings.json`
   (for example `"de": "..."`). Culture names follow .NET: `ru`, `de`, `pt-BR`, `zh-Hans`.
2. Add the language to the `catalogues` list in `tools/strings.mjs` and to
   `SatelliteResourceLanguages` in `Roomy.csproj`, then run `node tools/strings.mjs`.
3. If Windows reports the language under a different name than the catalogue
   (like `zh-CN` for `zh-Hans`), map it in `Loc.ResolveCulture`.

`ROOMY_LANG=ru` overrides the detected language, which is handy for testing:

```bash
ROOMY_LANG=ru dotnet run --project src/Roomy -- --demo
```

## Plural rules

`Loc.Plural` implements Russian (one/few/many) and Chinese (a single form);
add a branch there for languages with other rules before shipping their
catalogue. English falls back to one/many.
