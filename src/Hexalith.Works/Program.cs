using Hexalith.Works.Runtime;

WebApplication app = WorksHost.Build(args);

await app.RunAsync().ConfigureAwait(false);
