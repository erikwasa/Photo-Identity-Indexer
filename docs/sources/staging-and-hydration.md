# OneDrive hydration and staging

OneDrive Files On-Demand can expose placeholders whose content is not local. Discovery must therefore be separate from content availability.

The source adapter should distinguish local files, online-only placeholders, files being downloaded, offline failures and sync errors.

## Hydration modes

1. **User-managed hydration:** the user chooses **Always keep on this device** for the evaluation folder. This is the first implementation.
2. **Application-triggered hydration:** opening or copying a placeholder causes OneDrive to retrieve it; the application waits for a readable, complete file.
3. **Staging copy:** hydrated content is copied to a dedicated staging directory before local or Azure processing.

Staging protects against de-hydration, file locks, moves, source changes and interrupted bundle creation. Staged files are verified by size and hash and deleted only after processing or result import is verified.
