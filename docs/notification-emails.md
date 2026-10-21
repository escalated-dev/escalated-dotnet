# Notification emails

Escalated emails the people a ticket change concerns. The recipients are the ones the
Laravel reference's notification listeners pick, and nobody is emailed about their
own action.

| Event | Who gets an email |
|---|---|
| Ticket created | the requester, and the assignee if it was created assigned |
| Public reply | the requester unless they wrote it; the assignee unless they wrote it; followers other than the author, requester and assignee |
| Internal note | nobody (an @-mention emails the mentioned user) |
| Assigned | the agent, unless they assigned themselves |
| Status changed | the requester and followers, except whoever changed it |
| Escalated, SLA breached | the assignee |

Live chat tickets get no ticket, reply or status emails; the chat is the conversation.

## Turning it on

Nothing is sent until an SMTP server is configured. Until then, the first notification
logs `Escalated notification emails are off` once.

```json
{
  "Escalated": {
    "Email": {
      "Domain": "support.yourapp.com",
      "InboundSecret": "a-long-random-value",
      "FromAddress": "support@yourapp.com",
      "FromName": "YourApp Support",
      "Smtp": {
        "Host": "smtp.yourprovider.com",
        "Port": 587,
        "Username": "apikey",
        "Password": "...",
        "EnableSsl": true
      }
    }
  }
}
```

`FromAddress` defaults to `support@{Domain}`.

## Addresses

- **Requester:** the guest email, then the ticket's contact, then the host user's
  email from your `IUserDirectory`.
- **Agents and followers:** your `IUserDirectory`. Register one (`AddEscalated` ships
  an empty default), or only guests are emailed.

## Threading

Every email threads on the ticket:

- The new-ticket email carries `Message-ID: <ticket-{id}@{Domain}>`.
- A reply email carries `<ticket-{id}-reply-{replyId}@{Domain}>`, with `In-Reply-To`
  and `References` pointing at that anchor.
- Other notices point `In-Reply-To` and `References` at the anchor.

With `InboundSecret` set, `Reply-To` is the signed `reply+{id}.{hmac8}@{Domain}`
address, which [inbound email](../README.md#inbound-email) resolves to the ticket.

## Your own delivery

- **Another transport** (an email API, a queue): register an
  `IEscalatedMailTransport`. It receives each built `EmailMessage`.
- **Other recipients or another channel:** register an
  `IEscalatedNotificationSender`. Escalated then calls it for each event instead of
  the default `EmailNotificationSender`.
- **Off whatever the settings:** register `NullNotificationSender`.

Delivery happens while the change is saved. A failure is logged and never fails the change.
