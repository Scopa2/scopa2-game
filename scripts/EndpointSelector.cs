using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Scopa2Game.Scripts;

public partial class EndpointSelector : Node
{
    private const int PingTimeoutMs = 3000;
    private const int PingRetries = 3;

    public class EndpointMetrics
    {
        public ServerEndpoint Endpoint { get; set; }
        public double AveragePingMs { get; set; }
        public int SuccessfulPings { get; set; }
        public int FailedPings { get; set; }
        public double PacketLoss => (double)FailedPings / (SuccessfulPings + FailedPings) * 100;
        public bool IsReachable => SuccessfulPings > 0;
        
        public double Score => IsReachable ? AveragePingMs + (PacketLoss * 100) : double.MaxValue;
    }

    public async Task<ServerEndpoint> SelectBestEndpoint(ServerEndpoint[] endpoints)
    {
        var metrics = await GetEndpointMetrics(endpoints);
        
        var reachableMetrics = metrics.Where(m => m.IsReachable).ToArray();

        if (reachableMetrics.Length == 0)
        {
            GD.PrintErr("EndpointSelector: No reachable endpoints found! Falling back to first endpoint.");
            return endpoints[0];
        }

        var best = reachableMetrics.OrderBy(m => m.Score).First();
        
        GD.Print($"EndpointSelector: Selected {best.Endpoint.Region} ({best.Endpoint.Host}:{best.Endpoint.Port})");
        GD.Print($"  - Average Ping: {best.AveragePingMs:F2}ms");
        GD.Print($"  - Packet Loss: {best.PacketLoss:F2}%");
        GD.Print($"  - Score: {best.Score:F2}");

        return best.Endpoint;
    }

    public async Task<EndpointMetrics[]> GetEndpointMetrics(ServerEndpoint[] endpoints)
    {
        GD.Print("EndpointSelector: Testing endpoints...");
        
        var tasks = endpoints.Select(endpoint => MeasureEndpoint(endpoint)).ToArray();
        var metrics = await Task.WhenAll(tasks);

        return metrics;
    }

    private async Task<EndpointMetrics> MeasureEndpoint(ServerEndpoint endpoint)
    {
        var metrics = new EndpointMetrics 
        { 
            Endpoint = endpoint,
            SuccessfulPings = 0,
            FailedPings = 0
        };

        var pingTimes = new List<double>();

        for (int i = 0; i < PingRetries; i++)
        {
            var pingTime = await PingEndpoint(endpoint);
            
            if (pingTime >= 0)
            {
                metrics.SuccessfulPings++;
                pingTimes.Add(pingTime);
            }
            else
            {
                metrics.FailedPings++;
            }

            if (i < PingRetries - 1)
            {
                await Task.Delay(100);
            }
        }

        metrics.AveragePingMs = pingTimes.Count > 0 ? pingTimes.Average() : double.MaxValue;

        GD.Print($"EndpointSelector: {endpoint.Region} - Ping: {metrics.AveragePingMs:F2}ms, " +
                 $"Success: {metrics.SuccessfulPings}/{PingRetries}, Loss: {metrics.PacketLoss:F2}%");

        return metrics;
    }

    private async Task<double> PingEndpoint(ServerEndpoint endpoint)
    {
        var req = new HttpRequest();
        AddChild(req);

        var stopwatch = Stopwatch.StartNew();
        
        string pingUrl = $"http://{endpoint.Host}:{endpoint.Port}/api/health";
        
        string[] headers = { "Accept: application/json" };
        req.Request(pingUrl, headers, HttpClient.Method.Get);
        
        // Wait for completion
        var result = await req.ToSignal(req, HttpRequest.SignalName.RequestCompleted);

        stopwatch.Stop();
        req.QueueFree();
        
        // Check if timed out (based on elapsed time)
        if (stopwatch.Elapsed.TotalMilliseconds > PingTimeoutMs)
        {
            GD.Print($"EndpointSelector: {endpoint.Region} - Timeout");
            return -1;
        }

        long responseCode = result[1].AsInt64();
        
        // Accept 200 (success) or 404 (endpoint exists but no /health route)
        if (responseCode == 200 || responseCode == 404)
        {
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        return -1;
    }
}
