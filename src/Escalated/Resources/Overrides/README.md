# Plugin-local translation overrides

Strings dropped here win over the centrally shipped catalog from the
[`Escalated.Locale`](https://www.nuget.org/packages/Escalated.Locale)
NuGet package. Use this directory only for .NET-specific overrides
that should not be back-ported to the shared catalog.

## How it works

`AddEscalated()` registers a chained `IStringLocalizer` stack:

1. **Plugin-local** — `.resx` files placed in this directory.
2. **Central** — embedded JSON catalog from `Escalated.Locale`.

The first non-empty match wins. An override resx that defines a key
shadows the central translation for that key only; every other key
still resolves from the central catalog.

## File layout

Use the standard ASP.NET Core resx layout, mirroring the type or base
name you want to override. For example, to override the `Status_Open`
string for Italian:

```
Resources/Overrides/Messages.it.resx
```

The ResourcesPath for the default `IStringLocalizerFactory` is set to
`Resources/Overrides` in `EscalatedServiceCollectionExtensions`.

## When to add an override

- Wording reviewed by a host's localization team that should not
  affect other plugins.
- Branded vocabulary specific to a given deployment.
- Time-sensitive fix while a PR against
  [`escalated-locale`](https://github.com/escalated-dev/escalated-locale)
  is still in review.

In the steady state most installs will keep this directory empty and
inherit the canonical catalog.
