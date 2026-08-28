# Works v1 golden-payload corpus

These `*.v1.json` files are the **falsifiable Web-reader compatibility gate** for the Works raw-act
event catalog (RR-6 / NFR-12). Each file is a **concrete-type**, camelCase
`JsonSerializerDefaults.Web` sample with **no `$type`** discriminator. EventStore's shared reader
options are case-insensitive, so these historical client/wire samples remain readable alongside the
options-free PascalCase bytes at rest. Exact EventPersister bytes are frozen separately under
`../EventPersisterGolden/`.

Rules:

- **Every event ever produced must remain deserializable forever.** Never delete or mutate an existing
  frozen file's meaning.
- **Evolution is additive and nullable on the SAME record** (same discriminator / `FullName`). Adding a
  new optional field is fine; the corpus tests prove an unknown future field still deserializes.
- **Never mint a `…V2` type.** A version suffix is forbidden below version 2 by design; back-compat is
  achieved by additive fields, not new types.

`SchemaEvolutionGoldenCorpusTests` binds filenames bidirectionally to all 23 catalog event payload
types, deserializes each frozen file, round-trips it, and injects an unknown field to prove additive
tolerance. To add a new event to the compatibility corpus, serialize a representative instance with
`new JsonSerializerOptions(JsonSerializerDefaults.Web)` and freeze the output here.
