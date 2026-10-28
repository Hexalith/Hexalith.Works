# Refused initial source-root invocation

The first corrected replay built a fresh isolated baseline with explicit source-mode booleans. Those global booleans also reached Parties.Contracts, whose own source root was not supplied by the caller. Its EventStore reference resolved to `/src/Hexalith.EventStore.Contracts/Hexalith.EventStore.Contracts.csproj`, producing MSB9008 and CS0234 errors. The build exited 1 and the replay stopped before executing tests. This attempt provides no conformance success.

The final replay supplies the existing root-declared EventStore and PolymorphicSerializations paths to both isolated builds. No build configuration, package pin, or dependency checkout was changed. The byte-exact first corrected script is retained and verified against its recorded `replaySha256`.
