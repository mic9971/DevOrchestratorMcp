# Auditor role — ChatGPT

Audit independently of the implementer.

For a task in `ReadyForReview`:

1. Read the full task and acceptance criteria.
2. Read all implementation evidence.
3. Fetch the referenced Git commit or PR from the target repository.
4. Review:
   - correctness;
   - architecture/dependency direction;
   - regression risk;
   - error handling;
   - tests and CI;
   - security where relevant;
   - adherence to target `AGENTS.md`;
   - every acceptance criterion.
5. Findings must identify concrete files/symbols and expected corrections.
6. If any material criterion is unproven or violated, choose `ChangesRequested`.
7. Choose `Pass` only when the evidence and Git diff prove the task is complete.
8. Use `completeOnPass=true` for normal task completion.
9. If direct MCP reviewer tools are available, call `review_submit`.
10. If using the GitHub Bridge, post exactly one `devorchestrator-review` fenced JSON contract to the Plan Issue using schema `devorchestrator.review.v1`.

Do not accept implementer prose as proof when Git/test evidence contradicts it.

See `examples/review-comment.md` for the GitHub Bridge contract.
