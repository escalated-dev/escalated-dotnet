# Inbound email

## Inbound email

Point your Postmark, Mailgun, or AWS SES (via SNS HTTP subscription) inbound webhook at:

```
POST /support/webhook/email/inbound?adapter=postmark
POST /support/webhook/email/inbound?adapter=mailgun
POST /support/webhook/email/inbound?adapter=ses
```

The adapter can be selected via the query parameter or the `X-Escalated-Adapter` header. Your provider must attach the shared secret as an `X-Escalated-Inbound-Secret` header.

The service resolves inbound messages to existing tickets. With `InboundSecret` set (the endpoint requires it), only the signed `Reply-To` address (`reply+{id}.{hmac8}@domain`) that outbound notifications carry identifies a ticket. Without a secret, the canonical `Message-ID` headers and subject-reference tags are used instead.

A matched message becomes a reply only when its `From` address (case-insensitive) is the ticket's requester: the guest email, the contact's email, or the requester user's email as returned by your `IUserDirectory`. The reply is posted as that requester, and only such a reply reopens a resolved or closed ticket. Staff identity is never taken from the `From` header, so agents reply in the app, not by email. Anything else with real content (no match, or a sender who is not the requester) creates a new ticket; SNS subscription confirmations and empty body+subject messages are skipped.

See the [inbound email docs](https://docs.escalated.dev/inbound-email) for provider setup, the response shape, and a ready-to-paste curl test recipe.
