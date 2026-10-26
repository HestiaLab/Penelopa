using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Atlas.Blazor.DependencyInjection;
using Penelopa.Web;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Atlas.Blazor 工作区服务（工作区工厂、内容路由、历史）
builder.Services.AddAtlasWorkspace();

// 编辑器面板共享的图元存储和选区
builder.Services.AddSingleton<IPrimitiveService, PrimitiveService>();

// 仿真配置跨编辑页/仿真页保留；localStorage 保存边界由两个页面共享
builder.Services.AddSingleton<SimulationConfigStore>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddSingleton<DocumentImportRollbackService>();
builder.Services.AddScoped<DocumentFileService>();

await builder.Build().RunAsync();
