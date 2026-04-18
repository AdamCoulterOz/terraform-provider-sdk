using TerraformPluginDotnet.Hosting;
using TerraformProviderFile;

return await TerraformProviderHost.RunAsync(new FileProvider(), args);
