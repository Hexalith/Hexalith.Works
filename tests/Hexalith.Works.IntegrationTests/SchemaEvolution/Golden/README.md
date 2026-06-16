# Works v1 golden-payload corpus

These `*.v1.json` files are the **falsifiable back-compatibility gate** for the Works raw-act event
catalog (RR-6 / NFR-12). Each file is the **concrete-type**, `JsonSerializerDefaults.Web` serialized
form of a v1 event — exactly the bytes EventStore persists (camelCase, **no `$type`** discriminator;
the polymorphic `$type` form is a separate resolution capability, not the transport).

Rules:

- **Every event ever produced must remain deserializable forever.** Never delete or mutate an existing
  frozen file's meaning.
- **Evolution is additive and nullable on the SAME record** (same discriminator / `FullName`). Adding a
  new optional field is fine; the corpus tests prove an unknown future field still deserializes.
- **Never mint a `…V2` type.** A version suffix is forbidden below version 2 by design; back-compat is
  achieved by additive fields, not new types.

`SchemaEvolutionGoldenCorpusTests` deserializes each frozen file, asserts the reported field values,
round-trips it, and injects an unknown field to prove additive tolerance. To add a new event to the
corpus, serialize a representative instance with `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
and freeze the output here.
