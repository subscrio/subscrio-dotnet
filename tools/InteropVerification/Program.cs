using System.Text.Json;
using Subscrio.Core.Application.DTOs;

using var app=new Subscrio.Core.Subscrio(new(){Database=new(){ConnectionString=Environment.GetEnvironmentVariable("SUBSCRIO_INTEROP_CONNECTION")!},Clock=new FixedClock()});
var metadata=JsonSerializer.Deserialize<Dictionary<string,object?>>("""{"emoji":"🧪","unicode":"café","fraction":0.0000001,"nested":{"b":2,"a":null},"units":[1,true]}""")!;
var usage=await app.Metering.ReportUsageAsync("acme","studio","requests",2,new("interop-ts",Metadata:metadata));
if(usage.Usage.Consumed!=2)throw new Exception("TypeScript usage snapshot changed");
await app.Credits.GrantAsync(new("acme","credits",100,"manual","grant-ts",Metadata:metadata));
var spend=await app.Credits.ConsumeAsync(new("acme","render",3,"spend-ts",metadata));
if(spend.Balances[0].Available!=94)throw new Exception("TypeScript debit snapshot changed");
await app.Credits.AdjustAsync(new("acme","credits",-1,"correction","adjust-ts"));
await app.Metering.ReportUsageAsync("acme","studio","requests",3,new("interop-net",Metadata:metadata));
await app.Credits.GrantAsync(new("acme","credits",50,"prepaid","grant-net",Metadata:metadata));
await app.Credits.ConsumeAsync(new("acme","render",2,"spend-net",metadata));
await app.Credits.AdjustAsync(new("acme","credits",3,"correction","adjust-net"));
Console.WriteLine("DOTNET INTEROP VERIFIED");
sealed class FixedClock:IClock { public DateTime UtcNow=>new(2026,1,31,12,0,0,DateTimeKind.Utc); }
