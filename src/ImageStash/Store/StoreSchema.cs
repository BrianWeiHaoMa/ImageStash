namespace ImageStash.Store;

/// <summary>
/// The store's schema by version (F11, State: The store). <c>user_version</c> carries the version; a store below this
/// build's is brought up in one transaction that runs every later step and writes the version with them, so a kill
/// leaves the earlier version, which the next open brings up again. A later build appends a step and never edits one.
/// </summary>
internal static class StoreSchema
{
    /// <summary>Step <c>n</c> brings a store at version <c>n</c> to version <c>n + 1</c>.</summary>
    private static readonly string[] Steps =
    [
        // Version 1 carries every per-item fact State names, including those later builds first write (tags, favorite,
        // layers and their revisions, Delete's held copies, the Open with… session, the adoption's undo record), and
        // nothing else to file an item under (F9, N1). Times are UTC Unix milliseconds; the JSON columns hold lists or
        // records whose meaning lives with the code that writes them.
        """
        CREATE TABLE IF NOT EXISTS items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            favorite INTEGER NOT NULL DEFAULT 0,
            added_at INTEGER NOT NULL,
            format TEXT NOT NULL,
            width INTEGER NOT NULL,
            height INTEGER NOT NULL,
            animated INTEGER NOT NULL,
            frame_count INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL,
            original_file TEXT NOT NULL,
            original_in_originals INTEGER NOT NULL DEFAULT 0,
            finished_file TEXT NOT NULL,
            original_sha256 BLOB NOT NULL,
            layers TEXT NOT NULL DEFAULT '[]',
            previous_layers TEXT,
            layer_revision INTEGER NOT NULL DEFAULT 0,
            rendered_revision INTEGER NOT NULL DEFAULT 0,
            pending_steps TEXT,
            deleted INTEGER NOT NULL DEFAULT 0,
            held_copies TEXT,
            open_with_session TEXT,
            adoption_undo TEXT
        );
        CREATE INDEX IF NOT EXISTS items_by_original_sha256 ON items (original_sha256);
        CREATE TABLE IF NOT EXISTS tags (
            item_id INTEGER NOT NULL,
            tag TEXT NOT NULL,
            PRIMARY KEY (item_id, tag)
        );
        CREATE TABLE IF NOT EXISTS uses (
            item_id INTEGER NOT NULL,
            used_at INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS uses_by_item ON uses (item_id, used_at);
        """,

        // Version 2 keeps the finished picture's facts beside the original's record (format to duration_ms), which F3's
        // rule for an edited item reads and a Save never changes: what the render read back as, NULL while the item's
        // one file is its finished picture. Nullable without a default, so the rows an earlier build wrote are not
        // rewritten and read NULL.
        """
        ALTER TABLE items ADD COLUMN finished_format TEXT;
        ALTER TABLE items ADD COLUMN finished_width INTEGER;
        ALTER TABLE items ADD COLUMN finished_height INTEGER;
        ALTER TABLE items ADD COLUMN finished_animated INTEGER;
        ALTER TABLE items ADD COLUMN finished_frame_count INTEGER;
        ALTER TABLE items ADD COLUMN finished_duration_ms INTEGER;
        """,

        // Version 3 records every size upright: a JPEG's or WebP's original at the size its encoded origin turns it to,
        // and the facts of an edited item's render where version 1 recorded none. No column changes; the migration writes
        // the sizes RecordedSizePass read from the files before its transaction began.
        """
        SELECT 1;
        """,
    ];

    public static int CurrentVersion => Steps.Length;

    /// <summary>
    /// Brings a store at <paramref name="version"/> to <see cref="CurrentVersion"/> in one transaction, writing the
    /// <paramref name="sizes"/> read for it (<see cref="RecordedSizePass"/>) after every step and before the version.
    /// </summary>
    public static Outcome Migrate(StoreContext store, long version, IReadOnlyList<RecordedSizePass.Correction> sizes) =>
        store.Change(StoreOpen.Action, () =>
        {
            for (var step = (int)version; step < Steps.Length; step++)
            {
                store.Execute(Steps[step]);
            }

            RecordedSizePass.Write(store, sizes);
            store.Execute($"PRAGMA user_version = {CurrentVersion}");
        });
}
