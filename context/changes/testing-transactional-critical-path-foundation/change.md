---
change_id: testing-transactional-critical-path-foundation
title: Establish transactional critical-path test foundation
status: implemented
created: 2026-09-13
updated: 2026-09-13
archived_at: null
---

## Notes

Open a change folder for rollout Phase 1 of context/foundation/test-plan.md: "Transactional critical-path foundation".
Risks covered: #1 concurrent accepts or auto-accepts overfill an event's final slot; #2 an authenticated but unauthorized user obtains contact or event data by probing another event or request. Test types planned: unit + real-database integration.
Risk response intent:
- Risk #1: coordinated concurrent attempts never exceed capacity, and losing attempts receive the specified outcome.
- Risk #2: organizer, accepted participant, other request states, strangers, and unauthenticated callers each receive only permitted fields.
After creating the folder, follow the downstream continuation rule.
