using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SQS;
using CommonLib;
using ProcessorService;
using ProcessorService.Utilities;

var builder = Host.CreateApplicationBuilder(args);

//This is only for local. In Production use AWS System Manager Parameter Store or Secrets Manager to manage secrets
var awsOptions = new AWSOptions
{
    Region = RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"]),
    Credentials = new BasicAWSCredentials(
        builder.Configuration["AWS:AccessKeyId"],
        builder.Configuration["AWS:SecretAccessKey"])
};

builder.Services.AddDefaultAWSOptions(awsOptions);

var encryptKey = builder.Configuration["Encryption_TokenKey"];
if (string.IsNullOrEmpty(encryptKey))
{
    throw new InvalidOperationException("Encryption key is missing in configuration. Please check 'Encryption:TokenKey' in appsettings.json.");
}

builder.Services.AddSingleton<ITokenEncryptionService>(
    new EncryptDecryptToken(encryptKey));

builder.Services.AddHttpClient<GoogleApiTokenService>();
builder.Services.AddHostedService<ProcessorService.ProcessorService>();
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddTransient<HealthScoreCalculatorService>();

var host = builder.Build();
host.Run();
