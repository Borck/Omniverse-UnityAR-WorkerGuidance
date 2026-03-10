# Evidence Folder Structure (M12)

Updated: 2026-03-10

Store pilot evidence under `shared/fixtures/pilot-evidence/` to keep artifacts grouped by workflow.

## Layout
```text
shared/fixtures/pilot-evidence/
  PW-01/
    run-sheet.md
    diagnostics/
    recordings/
    server-logs/
  PW-02/
    run-sheet.md
    diagnostics/
    recordings/
    server-logs/
  PW-03/
    run-sheet.md
    diagnostics/
    recordings/
    server-logs/
  PW-04/
    run-sheet.md
    diagnostics/
    recordings/
    server-logs/
  PW-05/
    run-sheet.md
    diagnostics/
    recordings/
    server-logs/
```

## Usage
1. Copy `docs/validation/pilot-run-sheet-template.md` into each workflow folder as `run-sheet.md`.
2. Place diagnostics exports into `diagnostics/`.
3. Place screen recordings into `recordings/`.
4. Save server-side log extracts with matching `session_id` and `step_id` under `server-logs/`.
5. Link all artifact paths in each run sheet.
