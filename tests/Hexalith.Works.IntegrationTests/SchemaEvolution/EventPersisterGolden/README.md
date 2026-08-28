# Works v1 exact EventPersister corpus

The 23 `*.v1.json` files in this directory are raw event payload bytes returned by the pinned
`EventPersister` when it persists one `WorkItemV1Catalog` event in an isolated `DomainResult` with
payload protection disabled. They use compact options-free PascalCase JSON, contain no UTF-8 BOM or
trailing newline, and never contain a polymorphic `$type` discriminator.

These bytes were produced at EventStore pin `c61739206fd89619b7d29dfb0812225a234066bb`
(`v3.98.0-10-gc6173920`), the revision `references/Hexalith.EventStore` points at. `EventPersister.cs`
is unchanged from the earlier `b43e963403efa848eda9621b5e3e7e446c7faa2d`, so the frozen bytes hold at
both revisions.

`EventPersisterGoldenCorpusTests` binds filenames bidirectionally to all 14 success and 9 rejection
event types, then compares each file byte-for-byte with the real persister output. The separate
`../Golden/` directory preserves camelCase Web-reader compatibility history and is not a writer-byte
corpus.

To add a new event to the exact corpus, add its sample to `WorkItemV1Catalog` and freeze the bytes the
writer actually produces — `JsonSerializer.SerializeToUtf8Bytes(sample, sample.GetType())`, with no
options — as `<EventTypeName>.v1.json`, with **no** trailing newline and no BOM. Never hand-edit a
frozen file: a legitimate contract change is re-frozen from the writer, and the byte comparison in
`EveryExactFixtureEqualsTheRealEventPersisterPayloadBytes` is the check that the re-freeze is honest.
The repository `.editorconfig` sets `insert_final_newline = false` for this directory so editors do not
silently break the no-trailing-newline invariant.
