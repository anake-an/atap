### Fixed

- The local API keeps all its listeners after a pipe fails to open for a moment, so programs that push progress are not slowed down until Atap restarts.
- Sync tries a conflict copy again a few seconds later when it could not be opened, such as while a virus scan reads it, instead of waiting for the next change in the folder.
