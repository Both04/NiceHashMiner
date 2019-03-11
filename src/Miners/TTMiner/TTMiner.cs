﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinerPlugin;
using MinerPlugin.Toolkit;
using Newtonsoft.Json;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Enums;

namespace TTMiner
{
    public class TTMiner : MinerBase, IDisposable
    {
        private int _apiPort;
        private readonly string _uuid;
        private AlgorithmType _algorithmType;
        //private readonly Dictionary<int, int> _cudaIDMap;
        //private readonly HttpClient _http = new HttpClient();

        private class JsonApiResponse
        {
#pragma warning disable IDE1006 // Naming Styles
            public List<string> result { get; set; }
            public int id { get; set; }
            public object error { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        }

        private string AlgoName
        {
            get
            {
                switch (_algorithmType)
                {
                    case AlgorithmType.MTP:
                        return "mtp";
                    case AlgorithmType.Lyra2REv3:
                        return "LYRA2V3";
                    default:
                        return "";
                }
            }
        }

        private double DevFee
        {
            get
            {
                return 1.0;
            }
        }

        public TTMiner(string uuid)
        {
            _uuid = uuid;
        }

        public override async Task<(double speed, bool ok, string msg)> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            int benchTime;
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchTime = 20;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchTime = 120;
                    break;
                default:
                    benchTime = 60;
                    break;
            }

            var cl = CreateCommandLine(MinerToolkit.DemoUser);
            var (binPath, binCwd) = GetBinAndCwdPaths();
            var bp = new BenchmarkProcess(binPath, binCwd, cl);

            var benchHashes = 0d;
            var benchIters = 0;
            var benchHashResult = 0d;
            var targetBenchIters = 2; //Math.Max(1, (int)Math.Floor(benchTime / 20d));

            bp.CheckData = (data) =>
            {
                var (hashrate, found) = data.ToLower().TryGetHashrateAfter("]:");
                if (data.Contains("GPU[") && found && hashrate > 0)
                {
                    benchHashes += hashrate;
                    benchIters++;
                    benchHashResult = (benchHashes / benchIters) * (1 - DevFee * 0.01);
                }
                return (benchHashResult, benchIters >= targetBenchIters);
            };

            var timeout = TimeSpan.FromSeconds(benchTime + 5);
            var benchWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, timeout, benchWait, stop);
            return await t;
        }

        protected override (string binPath, string binCwd) GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "TT-Miner.exe");
            var binCwd = pluginRootBins;
            return (binPath, binCwd);
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username);
        }

        private string CreateCommandLine(string username)
        {
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange();
            var url = StratumServiceHelpers.GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.STRATUM_TCP);

            var devs = string.Join(" ", _miningPairs.Select(p => p.device.ID).OrderBy(id => id));
            var cmd = $"-a {AlgoName} -url {url} " +
                              $"-u {username} -d {devs} --api-bind 127.0.0.1:{_apiPort} ";

            // TODO
            //cmd += ExtraLaunchParameters

            return cmd;
        }

        public override async Task<ApiData> GetMinerStatsDataAsync()
        {
            var api = new ApiData();
            JsonApiResponse resp = null;
            try
            {
                var bytesToSend = Encoding.ASCII.GetBytes("{\"id\":0,\"jsonrpc\":\"2.0\",\"method\":\"miner_getstat1\"}\n");
                using (var client = new TcpClient("127.0.0.1", _apiPort))
                using (var nwStream = client.GetStream())
                {
                    await nwStream.WriteAsync(bytesToSend, 0, bytesToSend.Length);
                    var bytesToRead = new byte[client.ReceiveBufferSize];
                    var bytesRead = await nwStream.ReadAsync(bytesToRead, 0, client.ReceiveBufferSize);
                    var respStr = Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
                    //Helpers.ConsolePrint(MinerTag(), "respStr: " + respStr);
                    resp = JsonConvert.DeserializeObject<JsonApiResponse>(respStr);
                    // TODO
                    //api.AlgorithmSpeedsTotal = new[] { (_algorithmType, resp.TotalHashrate ?? 0) };
                }
            }
            catch (Exception ex)
            {
                //Helpers.ConsolePrint(MinerTag(), "GetSummary exception: " + ex.Message);
            }

            return api;
        }

        protected override void Init()
        {
            bool ok;
            (_algorithmType, ok) = _miningPairs.GetAlgorithmSingleType();
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");
        }

        public void Dispose()
        {
        }
    }
}
