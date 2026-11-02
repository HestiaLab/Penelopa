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

// 多文档会话：每个会话独立模型/历史/配置；现有组件通过活动代理访问当前文档。
builder.Services.AddSingleton<DocumentSessionManager>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().ActiveProxy);
builder.Services.AddSingleton<IPrimitiveService>(
    sp => sp.GetRequiredService<DocumentSessionManager>().ActiveProxy);
// 所有文档写入的唯一入口；内部转发到活动会话。
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().Editor);

// 活动会话的仿真配置、保存边界、journal 与导入回滚。
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().ConfigStore);
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().Storage);
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().Journal);
builder.Services.AddSingleton(sp => sp.GetRequiredService<DocumentSessionManager>().ImportRollback);
builder.Services.AddScoped<DocumentFileService>();

await builder.Build().RunAsync();
