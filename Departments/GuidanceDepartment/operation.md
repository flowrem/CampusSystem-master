Proposal: Extending the University Guidance System for Multi-Department Operation


RULE: just analyze then state the ups and downs. Also provide to add plans for workarounds what needs to be added for a campus  guidance department in order to operate



Reference architecture: University Guidance Department System — System Integration & Architecture Prepared for: Guidance Department leadership / project stakeholders Status: For review

1. Purpose
The current system was designed and built for a single department: Guidance. Other campus departments (e.g., Health Services, Registrar, Financial Aid) are expected to adopt the same platform. This proposal defines the minimum set of changes required so Guidance can operate correctly alongside other departments on shared infrastructure, without compromising student privacy, workflow integrity, or the security guarantees already established in the architecture.
This is a scoping and rationale document — it recommends what to build and why, not implementation code.

2. Background
The existing architecture (Sections 3.A–3.D of the reference document) already provides a strong, reusable foundation:
Role-based access control ([Authorize], Student/Counselor roles)
Ownership enforcement scoped to StudentId
Audit logging of state transitions (Requested → Resolved)
PII masking on export
CSVHelper-based bulk import with dry-run validation
Polly circuit breaking for outbound communication
In-app messaging as the counselor sync method
Rate limiting on authentication and request endpoints
None of this was designed with more than one department in mind. There is currently:
No Department concept anywhere in the data model
No way to scope a role to a specific department (a Counselor role has no boundary preventing it from acting on non-Guidance data if another department's records entered the same schema)
A two-state workflow (Requested → Resolved) with no way to hand a request from one department to another
If a second department is onboarded onto this system as-is, there is a real risk of cross-department data exposure and workflow ambiguity — a Guidance counselor could technically see or act on Health Services or Registrar requests, and vice versa.

3. Problem Statement
The system has no departmental boundary. Every access control, audit, masking, and messaging mechanism currently assumes a single department context. Adding a second department without addressing this exposes student data across departmental lines and breaks the "ownership enforcement" guarantee the architecture already promises for StudentId.
Additionally, counselors have a real, recurring workflow need — referring a student to another office (e.g., "this is a Financial Aid issue, not Guidance") — that the current two-state model cannot represent at all.

4. Proposed Solution
4.1 Core addition: a Department concept
Introduce Department as a first-class attribute across the data model and access layer:
Every request/record is tagged with an owning department.
Roles become department-scoped (Counselor@Guidance rather than a bare Counselor).
[Authorize] checks are extended to validate department scope in addition to role — the same mechanism used today for StudentId ownership enforcement, applied one level higher.
This is a parameterization of existing mechanisms, not new architecture — RBAC, ownership enforcement, audit logging, PII masking, CSV export, Polly policies, messaging, and rate limiting all already have a natural place to accept a department dimension. The proposal is to add it consistently across all of them rather than let each department maintain a separate fork of the system.
4.2 Core addition: a Referred workflow state
Extend the state machine from Requested → Resolved to include Referred:
A Refer/Route action, distinct from the existing internal Escalate action (which stays within Guidance to a senior counselor).
A referral notifies the receiving department's queue without exposing full case history by default — only what's explicitly shared at referral time.
Referral events are captured in the audit log using the same non-policing event-tracking pattern already in place for Requested → Resolved.
This is the one genuinely new piece of workflow logic — everything else in this proposal is scope, not new state.

5. Scope
In scope
Department field/entity across data model, RBAC, audit log, PII masking rules, CSV export, messaging, and rate limiting
Referred state and Refer/Route action
Department-scoped admin configuration (assigned staff, escalation contacts, routing rules) — minimal, per-department settings only
Cross-department referral notification (in-app, consistent with the existing messaging trade-off)
Out of scope (no changes proposed)
JWT handling, token expiry, HttpOnly/SameSite cookie configuration
FluentValidation / strong-typed DTO pattern
HtmlSanitizer for Safety Valve fields
Encryption at rest (TDE)
Idempotency-Key handling
EF Core row-versioning / concurrency control
Mobile PWA input engine
These remain exactly as specified in the current architecture and require no modification to support multiple departments.

6. Implementation Approach
Recommended as an incremental extension of the existing roadmap (Section 6 of the reference document), not a rebuild:
Phase
Work
A
Add Department field to schema and DTOs; extend [Authorize] and ownership enforcement to check department scope
B
Add Referred state to the workflow; implement Refer/Route action and audit event
C
Extend PII masking, CSV export, and rate-limiting configuration to be department-aware
D
Department-scoped admin surface for staff assignment and routing rules; cross-department notification wiring

This sequencing keeps Guidance fully functional at every phase — department-scoping is additive and backward-compatible with the current single-department behavior.

7. Risks and Considerations
Data leakage during transition: until department scoping is enforced everywhere (Phase A/C), a second department's data should not be introduced into the shared schema.
Referral over-sharing: the Referred action must default to minimal disclosure — this needs an explicit decision on what fields transfer with a referral versus what stays private to the originating department.
Admin surface creep: the department-scoped admin panel should stay minimal (staff assignment, routing rules) to avoid re-introducing the complexity the "Concierge Model" trade-off was designed to avoid.

8. Recommendation
Approve Phases A and B as the minimum viable extension before any second department is onboarded. Phases C and D can follow once a second department's actual requirements (e.g., Health Services' likely stricter PII sensitivity tier) are confirmed, so the department-aware masking and admin rules are built against real requirements rather than assumptions.

9. Summary
Item
Type
Effort relative to existing system
Department field across existing services
Extension/parameterization
Low–Medium
Referred state + Refer/Route action
New workflow logic
Medium
Department-scoped admin surface
New, minimal
Low
Everything else in the current architecture


Unchanged
None

This was based on this template BEWEARE you cannot change whats in the template no matter what
