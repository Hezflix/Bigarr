# Radarr webhook samples

What Radarr's webhook sends for the fixture of `RadarrWebhookTests`, one file per payload. They were written by Radarr's
own payload classes (`src/NzbDrone.Core/Notifications/Webhook/Webhook*Payload.cs` and their members, `develop` branch)
serialized with Radarr's settings (`NzbDrone.Common.Serializer.Json.GetSerializerSettings()`: Newtonsoft, camelCase,
nulls left out, enums as camelCase strings, `eventType` in PascalCase), the members filled the way
`WebhookBase.Build*Payload` fills them. Radarr's domain types the payloads read from (`Movie`, `MovieFile`,
`RemoteMovie`...) were replaced by the fixture's values, so the samples pin the wire shape, not Radarr's lookups.

Regenerate them the same way when Radarr changes a payload; never edit them by hand to make a test pass.
