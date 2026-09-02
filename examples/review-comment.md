# Example GitHub Review Comment

After ChatGPT Web audits the implementation PR, post one machine-readable review comment on the plan issue.

Pass:

```devorchestrator-review
{
  "schema": "devorchestrator.review.v1",
  "taskCode": "P2-001",
  "decision": "Pass",
  "summary": "Acceptance criteria, build and tests were verified.",
  "findings": [],
  "completeOnPass": true
}
```

Request changes:

```devorchestrator-review
{
  "schema": "devorchestrator.review.v1",
  "taskCode": "P2-001",
  "decision": "ChangesRequested",
  "summary": "One required fix remains.",
  "findings": [
    "Add a regression test for the changed behavior."
  ],
  "completeOnPass": true
}
```

Then Codex calls:

```text
bridge_sync_reviews(projectKey="novel-platform", issueNumber=<issue-number>)
```
