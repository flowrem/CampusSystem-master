# Campus Review Swarm Plan

## Goal
Build a review-only swarm for the CampusSystem maintenance dashboard. The swarm is designed to analyze the system using department context files, health data, and operational status without writing code or altering project files.

## Scope
- Read-only review flow
- No project file writes
- No code generation
- No start/stop/build execution
- Focus on review, risk analysis, and report synthesis

## Agent model
Use six department-based agents:

1. StudentPortal
2. GuidanceDepartment
3. Finance
4. Library
5. Registrar
6. FacultyPortal

Then synthesize with a final review step.

## Input sources
- Maintenance AI context file
- Each department AI context file
- Current health/error report when available

## Output
A single Markdown review report with:
- Summary
- Department findings
- Cross-department risks
- Open items
- Recommendations

## Safety rules
- No writes outside the report output
- No changes to any source code
- No execution of department apps
- No file access or code generation
- The swarm only reviews and summarizes

## Default config shape
```json
{
  "defaultProvider": "google",
  "googleApiKey": "YOUR_GOOGLE_API_KEY",
  "googleModel": "gemini-3.1-flash-lite",
  "reviewOnlyMode": true,
  "swarmFileAccessEnabled": false,
  "coderModeEnabled": false,
  "agents": [
    { "id": "studentportal", "name": "StudentPortal" },
    { "id": "guidancedepartment", "name": "GuidanceDepartment" },
    { "id": "finance", "name": "Finance" },
    { "id": "library", "name": "Library" },
    { "id": "registrar", "name": "Registrar" },
    { "id": "facultyportal", "name": "FacultyPortal" }
  ],
  "rounds": 3
}
```

## Notes
This is intentionally not an Obsidian coding swarm. It is a maintenance review swarm focused on evidence, consistency, and operational risk assessment.
