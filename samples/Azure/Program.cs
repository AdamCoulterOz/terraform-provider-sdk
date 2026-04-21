using TerraformPluginDotnet.Hosting;
using Azure;

return await TerraformProviderHost.RunAsync(new Provider(), args);
