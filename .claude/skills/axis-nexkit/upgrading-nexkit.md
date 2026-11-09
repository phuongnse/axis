# Upgrading NexKit for Axis

An upgrade is one hand-made pull request. Follow NexKit's upgrade steps in its
configuration doc, then:

1. Name every key left at its default in the pull request, with the reason.
2. Change `docs/delivery.md` and the [`axis-nexkit`](SKILL.md) skill wherever
   the release changes a setting Axis chooses or a choice NexKit leaves to Axis.
3. Say in the pull request what changes, what the release brings that needs
   nothing here, and what the owner does: merge once `/nexkit review` has set
   `nexkit/checks` and `nexkit/review` green, and any secret, permission or
   setting only the owner can add. Comment `/nexkit review` after each push,
   as NexKit's upgrade steps say.
