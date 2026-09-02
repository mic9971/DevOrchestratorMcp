# Example GitHub Plan Issue

ChatGPT Web can create an issue like this in the registered target repository.

```devorchestrator-plan
{
  "schema": "devorchestrator.plan.v1",
  "projectKey": "novel-platform",
  "tasks": [
    {
      "code": "P2-001",
      "title": "Add repository contract",
      "objective": "Implement one small isolated change.",
      "acceptanceCriteria": [
        "Release build passes",
        "Relevant automated tests pass"
      ],
      "constraints": [
        "Do not change unrelated behavior"
      ],
      "dependencies": [],
      "priority": "Normal"
    },
    {
      "code": "P2-002",
      "title": "Consume repository contract",
      "objective": "Implement the dependent behavior.",
      "acceptanceCriteria": [
        "P2-001 is complete",
        "Release build passes"
      ],
      "dependencies": ["P2-001"],
      "priority": "Normal"
    }
  ]
}
```

After creating the issue, Codex calls:

```text
bridge_import_plan_issue(projectKey="novel-platform", issueNumber=<issue-number>)
```
