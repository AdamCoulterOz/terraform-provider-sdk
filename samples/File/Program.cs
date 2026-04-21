using TerraformPluginDotnet.Hosting;
using File;

return await TerraformProviderHost.RunAsync(new FileProvider(), args);
