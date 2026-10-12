# Testing Data

Testing owns no collections. It is a browser and editor for the shared JSON datastore (`campus_system_unified_data_schema.json`).

## What the page does

- Lists every array collection under `unified_seed_data` with its row count.
- Shows the rows of the selected collection, with the union of all columns.
- Adds a row from JSON (an id is assigned if missing; a given id must be a free whole number).
- Edits a row's scalar fields. Each value keeps the type the field already has, and bad input is rejected without changing the file. Objects and arrays are left alone.
- Deletes a row by id. Rows with no numeric id (such as `settings`) can be viewed but not edited here.

Every write goes through `CanonicalJsonStore.UpdateRoot`, so it is safe beside running department apps. It is also live: edits change the real data, so use `Reset-CampusJsonData.ps1` to go back to the seed.

## Code

`Services/JsonCollectionBrowser.cs` and `Pages/Index.cshtml(.cs)`.

## Tests

`dotnet test Departments\Testing\TestingMain.Tests`
