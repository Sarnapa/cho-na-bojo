---
project: "ChoNaBojo"
context_type: greenfield
product_type: mobile
target_scale:
  users: medium
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: 2026-08-01
  after_hours_only: true
created: 2026-05-23
updated: 2026-05-23
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "missing capability — no good way to find nearby sports partners"
    - topic: "insight vs existing solutions"
      decision: "existing tools don't focus on bringing people together at nearby sports facilities"
    - topic: "primary persona scope"
      decision: "individuals across many cities/regions"
    - topic: "auth strategy"
      decision: "login required (email+password / OAuth / passwordless — exact method TBD at stack selection)"
    - topic: "role model"
      decision: "per-event roles (organizer vs participant); no global admin in MVP"
    - topic: "scope-down decision"
      decision: "auth simplified to email+password only; map and push notifications kept as core"
    - topic: "MVP timeline"
      decision: "3 weeks after-hours"
    - topic: "business logic rule"
      decision: "app surfaces venues and events within map viewport, filtered by sport preference and time availability"
    - topic: "NFRs"
      decision: "map < 2s, contact info privacy, push < 30s, Android 10+"
    - topic: "product type"
      decision: "mobile app with backend server"
    - topic: "target scale"
      decision: "dozens to a hundred for MVP launch"
    - topic: "timeline"
      decision: "hard deadline 2026-08-01, after-hours only"
  frs_drafted: 14
  quality_check_status: accepted
---

## Vision & Problem Statement

Recreational athletes want to play team sports in their neighborhood but can't find enough players among their friends. Existing tools (Meetup, Facebook groups, sport-specific apps) don't focus on bringing people together at nearby sports facilities. ChoNaBojo connects nearby players around specific venues, filling the roster gap and creating chances to meet like-minded people — without crossing the city.

Pain category: missing capability — there is no good way to find nearby sports partners at local facilities.

## User & Persona

### Primary persona

A recreational athlete in any city, with free time and a sport in mind, looking for nearby players at a local facility. They are open to meeting strangers who share their sport interest. They don't want to travel across the city — they want to play in their neighborhood.

Scope: individuals across many cities and regions.

## Access Control

Auth: Login required (email+password / OAuth / passwordless — exact method TBD at stack selection).

Roles: Per-event, not per-account. Any user can be an organizer (for events they create) and a participant (for events they join). No global admin role in MVP.

Unauthenticated users cannot browse or interact with events.

## Success Criteria

### Primary

- A user creates an event at a nearby venue and at least one other user joins and gets accepted — proving the matchmaking loop works.

### Secondary

- A user joins someone else's event, it happens, and they meet a new person who shares their sport interest.

### Guardrails

- Contact info (phone, email, messenger) is only revealed after explicit organizer acceptance — never to unapproved requesters.
- Event data (location, time, participants) is not accessible to unauthenticated users.

## Functional Requirements

### Authentication

- FR-001: User can create an account with email and password. Priority: must-have
  > Socrates: No counter-argument; it stands as written.

- FR-002: User can log in to their account. Priority: must-have
  > Socrates: Counter-argument considered: "Login without session persistence forces re-auth too often." Resolution: kept; session persistence (stay logged in on mobile) is an implicit requirement.

### Sport & Venue Discovery

- FR-003: User can optionally filter venues and events by sport discipline from a predefined list; all events are shown by default. Priority: must-have
  > Socrates: Counter-argument considered: "Forcing sport selection first slows down users who just want to browse nearby events regardless of sport." Resolution: revised — sport selection is now an optional filter, not a mandatory first step.

- FR-004: User can view a map centered on their current location showing nearby venues for the selected sport. Priority: must-have
  > Socrates: Counter-argument considered: "Map requires location permission — users who deny it get a broken experience." Resolution: kept; a fallback for denied location permission (e.g., manual city/address entry) is needed.

### Event Management

- FR-005: User can create an event at a selected venue, specifying date, estimated end time, participant limit, and optionally enabling auto-accept for join requests. Priority: must-have
  > Socrates: Counter-argument considered: "No minimum participant count means events with 1 slot are pointless." Resolution: kept; minimum participant limit should be enforced (≥ 2). Auto-accept is an optional organizer preference.

- FR-006: User can view existing events at a selected venue and request to join one. Priority: must-have
  > Socrates: Counter-argument considered: "No preview of who's already in the event — joining is a blind leap." Resolution: kept; users should see how many spots are filled vs. the limit before requesting to join.

- FR-007: User cannot join an event once its estimated end time has passed. Priority: must-have
  > Socrates: Revised from "cannot join after start" to "cannot join after estimated end time" — late joining after start is permitted.

### Approval Workflow

- FR-008: Organizer receives a push notification when someone requests to join their event. Priority: must-have
  > Socrates: Counter-argument considered: "Approval gates slow down matchmaking — auto-accept would fill events faster." Resolution: kept; auto-accept is an optional feature for the organizer (captured in FR-005).

- FR-009: Organizer can accept or reject a join request. Priority: must-have
  > Socrates: See FR-008 resolution. Approval flow stands; auto-accept is an optional alternative.

- FR-010: Participant receives a push notification about acceptance or rejection. Priority: must-have
  > Socrates: See FR-008 resolution. Stands as written.

### Contact & Coordination

- FR-011: Upon acceptance, both organizer and participant can see each other's contact info (phone, email, or messenger handle — at least one required, not all). Priority: must-have
  > Socrates: Counter-argument considered: "Users might not have filled in contact details." Resolution: kept; at least one contact method is required during registration, but user chooses which ones to provide.

### Event Lifecycle

- FR-012: Organizer can cancel their event; all participants receive a push notification. Priority: must-have
  > Socrates: No counter-argument; it stands as written.

- FR-013: Organizer can remove a participant (even after acceptance); the participant is notified. Priority: must-have
  > Socrates: No counter-argument; it stands as written.

- FR-014: Participant can leave an event (even after acceptance); the organizer is notified. Priority: must-have
  > Socrates: No counter-argument; it stands as written.

## User Stories

### US-01: User finds nearby players and joins a sports event

- **Given** a logged-in user who has selected a sport discipline
- **When** they open the map, tap a nearby venue, and request to join an existing event
- **Then** the organizer is notified, and upon acceptance, both parties see each other's contact info to coordinate details

#### Acceptance Criteria

- Map centers on user's current location
- Only venues for the selected sport are shown
- Join request triggers a push notification to the organizer
- Contact info is revealed only after explicit acceptance
- User cannot join an event that has already started

### US-02: User creates an event and approves a participant

- **Given** a logged-in user who has selected a sport and a venue
- **When** they create an event (setting date, end time, participant limit), and another user requests to join
- **Then** the organizer receives a push notification, can accept the request, and both parties see each other's contact info

#### Acceptance Criteria

- Event appears on the venue for other users to discover
- Organizer sets date, estimated end time, and participant limit
- Join request generates a push notification to the organizer
- Organizer can accept or reject each request individually
- Contact info is revealed only after acceptance
- Event cannot accept joins after it has started

## Business Logic

The app surfaces sports venues and their events within the user's current map viewport, filtered by optional sport preference and time availability.

Inputs: Map viewport (position + zoom level), optional sport filter and time availability.

Output: Venues visible on the current map view. Panning/zooming dynamically reveals more venues. Selecting a venue shows events available to join (not past end time, spots open) or the option to create one.

How the user encounters it: Open the map (centered on their location) → see venues in view → pan to explore further → tap a venue → see joinable events or create their own. The map viewport is the proximity boundary; moving it reveals new options.

## Non-Functional Requirements

- Map loads and responds to pan/zoom within 2 seconds.
- Contact info (phone, email, messenger) is never visible to unapproved users — strict privacy boundary.
- Push notifications arrive within 30 seconds of the triggering action.
- App works on Android 10+ (API level 29+).

## Non-Goals

- No user-contributed venue additions — the app uses a predefined venue database only. Rationale: venue curation is a separate product concern; a fixed list keeps the MVP scope tight.
- No inviting specific users to an event — discovery is open, not social-graph-based. Rationale: the product's value is meeting NEW people, not coordinating with existing contacts.
- No user rating or reputation system — no trust scoring in MVP. Rationale: adds significant complexity; defer until scale demands it.
- No in-app messaging — users coordinate via external messengers after contact info reveal. Rationale: building a messenger is a product unto itself; contact sharing is sufficient for MVP.
- No iOS support — Android-only for MVP. Rationale: single-platform focus reduces development and testing scope by half.

## Quality cross-check

All elements present — no gaps. Status: accepted.

## Forward: scale features

At 100x scale (thousands of users), the product would need: user rating system, reporting system for user behavior concerns, in-app messaging within event spaces, ability to invite specific users, and capacity warnings. These are post-MVP features for future iterations.
