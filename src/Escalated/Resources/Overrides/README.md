# Plugin-local translation overrides

Strings embedded here win over the shared JSON catalog shipped inside the
Escalated assembly. Use this directory for .NET-specific overrides
that should not be back-ported to the shared catalog.

## How it works

`AddEscalated()` registers a chained `IStringLocalizer` stack:

1. **Plugin-local** — `.resx` files placed in this directory.
2. **Optional shared provider** — the `Escalated.Locale` localizer, when installed.
3. **Embedded catalog** — the vendored `Resources/locales/*.json` files.

The first found match wins, including an intentionally empty string. An override resx that defines a key
shadows the central translation for that key only; every other key
still resolves from the central catalog.

## File layout

Use the standard ASP.NET Core resx layout in the assembly containing the type
or base name you pass to the localizer factory. For a `Messages` resource type
directly under that assembly's root namespace, an Italian override for the
catalog key `status.open` belongs at:

```
Resources/Overrides/Messages.it.resx
```

The ResourcesPath for the default `IStringLocalizerFactory` is set to
`Resources/Overrides` in `EscalatedServiceCollectionExtensions`.

Nested resource-type namespaces add matching directories. For example, a
`MyApp.Localization.Messages` type in `MyApp` uses
`Resources/Overrides/Localization/Messages.it.resx`. These files must be
embedded when that assembly is built.

An absent override manifest is treated as an empty layer when enumerating
strings. Lookup and enumeration continue to the embedded catalogs, following
the current UI culture, its parents, and English. Use `GetAllStrings(true)`
to include those fallback cultures.

## When to add an override

- Wording reviewed by a host's localization team that should not
  affect other plugins.
- Branded vocabulary specific to a given deployment.
- Time-sensitive fix while a PR against
  [`escalated-locale`](https://github.com/escalated-dev/escalated-locale)
  is still in review.

In the steady state most installs will keep this directory empty and
inherit the canonical catalog.
