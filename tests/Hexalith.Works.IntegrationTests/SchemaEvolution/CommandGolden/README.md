# Works v1 command golden payloads

This directory freezes exact options-free PascalCase command bytes for additive command-contract
changes that require an explicit compatibility gate. Files contain no UTF-8 BOM, trailing newline,
or polymorphic discriminator. `LinkConversationCommandGoldenTests` compares the fixture byte-for-byte
with the concrete `System.Text.Json` writer and reads it through EventStore's shared tolerant reader.
