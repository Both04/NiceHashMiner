﻿using Newtonsoft.Json;
using NiceHashMiner.Configs;
using NiceHashMiner.Enums;
using NiceHashMiner.Miners.Grouping;
using NiceHashMiner.Miners.Parsing;
using NiceHashMiner.Net20_backport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NiceHashMiner.Miners {
    public abstract class ClaymoreBaseMiner : Miner {

        const int benchmarkTimeWait = 45; // if this is larger then welcome to hell!!! keep this fixed
        int benchmark_read_count = 0;
        double benchmark_sum = 0.0d;
        protected readonly string LOOK_FOR_START;
        protected readonly string MinerExeName;
        const string LOOK_FOR_END = " h/s";


        public ClaymoreBaseMiner(string minerDeviceName, string minerPath, string minerExeName, string look_FOR_START)
            : base(minerDeviceName) {
            Path = minerPath;
            MinerExeName = minerExeName;
            LOOK_FOR_START = look_FOR_START.ToLower();
            WorkingDirectory = minerPath.Replace(minerExeName + ".exe", "");
            IsKillAllUsedMinerProcs = true;
        }

        protected override int GET_MAX_CooldownTimeInMilliseconds() {
            return 60 * 1000 * 5; // 5 minute max, whole waiting time 75seconds
        }

        private class JsonApiResponse {
            public List<string> result { get; set; }
            public int id { get; set; }
            public object error { get; set; }
        }

        protected void KillClaymoreMinerBase(string exeName) {
            foreach (Process process in Process.GetProcessesByName(exeName)) {
                try { process.Kill(); } catch (Exception e) { Helpers.ConsolePrint(MinerDeviceName, e.ToString()); }
            }
        }

        public override APIData GetSummary() {
            _currentMinerReadStatus = MinerAPIReadStatus.NONE;
            APIData ad = new APIData(MiningSetup.CurrentAlgorithmType);

            TcpClient client = null;
            JsonApiResponse resp = null;
            try {
                byte[] bytesToSend = ASCIIEncoding.ASCII.GetBytes("{\"id\":0,\"jsonrpc\":\"2.0\",\"method\":\"miner_getstat1\"}n");
                client = new TcpClient("127.0.0.1", APIPort);
                NetworkStream nwStream = client.GetStream();
                nwStream.Write(bytesToSend, 0, bytesToSend.Length);
                byte[] bytesToRead = new byte[client.ReceiveBufferSize];
                int bytesRead = nwStream.Read(bytesToRead, 0, client.ReceiveBufferSize);
                string respStr = Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
                resp = JsonConvert.DeserializeObject<JsonApiResponse>(respStr, Globals.JsonSettings);
                client.Close();
                //Helpers.ConsolePrint("ClaymoreZcashMiner API back:", respStr);
            } catch (Exception ex) {
                Helpers.ConsolePrint(this.MinerTAG(), "GetSummary exception: " + ex.Message);
            }

            if (resp != null && resp.error == null) {
                //Helpers.ConsolePrint("ClaymoreZcashMiner API back:", "resp != null && resp.error == null");
                if (resp.result != null && resp.result.Count > 4) {
                    //Helpers.ConsolePrint("ClaymoreZcashMiner API back:", "resp.result != null && resp.result.Count > 4");
                    var speeds = resp.result[3].Split(';');
                    ad.Speed = 0;
                    foreach (var speed in speeds) {
                        //Helpers.ConsolePrint("ClaymoreZcashMiner API back:", "foreach (var speed in speeds) {");
                        double tmpSpeed = 0;
                        try {
                            tmpSpeed = Double.Parse(speed, CultureInfo.InvariantCulture);
                        } catch {
                            tmpSpeed = 0;
                        }
                        ad.Speed += tmpSpeed;
                    }
                    _currentMinerReadStatus = MinerAPIReadStatus.GOT_READ;
                }
                if (ad.Speed == 0) {
                    _currentMinerReadStatus = MinerAPIReadStatus.READ_SPEED_ZERO;
                }
            }

            return ad;
        }

        protected override bool UpdateBindPortCommand(int oldPort, int newPort) {
            const string MASK = "-mport -{0}";
            var oldApiBindStr = String.Format(MASK, oldPort);
            var newApiBindStr = String.Format(MASK, newPort);
            if (LastCommandLine != null && LastCommandLine.Contains(oldApiBindStr)) {
                LastCommandLine = LastCommandLine.Replace(oldApiBindStr, newApiBindStr);
                return true;
            }
            return false;
        }

        protected override void _Stop(MinerStopType willswitch) {
            Stop_cpu_ccminer_sgminer_nheqminer(willswitch);
        }

        private bool IsActiveProcess(int pid) {
            try {
                return Process.GetProcessById(pid) != null;
            } catch {
                return false;
            }
        }


        protected override string GetDevicesCommandString() {
            string extraParams = ExtraLaunchParametersParser.ParseForMiningSetup(MiningSetup, DeviceType.AMD);
            string deviceStringCommand = " -di ";
            List<string> ids = new List<string>();
            foreach (var mPair in MiningSetup.MiningPairs) {
                ids.Add(mPair.Device.ID.ToString());
            }
            deviceStringCommand += StringHelper.Join("", ids);

            return deviceStringCommand + extraParams;
        }

        // benchmark stuff

        protected override void BenchmarkThreadRoutine(object CommandLine) {
            Thread.Sleep(ConfigManager.GeneralConfig.MinerRestartDelayMS);

            BenchmarkSignalQuit = false;
            BenchmarkSignalHanged = false;
            BenchmarkSignalFinnished = false;
            BenchmarkException = null;

            try {
                Helpers.ConsolePrint("BENCHMARK", "Benchmark starts");
                Helpers.ConsolePrint(MinerTAG(), "Benchmark should end in : " + benchmarkTimeWait + " seconds");
                BenchmarkHandle = BenchmarkStartProcess((string)CommandLine);
                BenchmarkHandle.WaitForExit(benchmarkTimeWait + 2);
                Stopwatch _benchmarkTimer = new Stopwatch();
                _benchmarkTimer.Reset();
                _benchmarkTimer.Start();
                //BenchmarkThreadRoutineStartSettup();
                // wait a little longer then the benchmark routine if exit false throw
                //var timeoutTime = BenchmarkTimeoutInSeconds(BenchmarkTimeInSeconds);
                //var exitSucces = BenchmarkHandle.WaitForExit(timeoutTime * 1000);
                // don't use wait for it breaks everything
                BenchmarkProcessStatus = BenchmarkProcessStatus.Running;
                bool keepRunning = true;
                while (keepRunning && IsActiveProcess(BenchmarkHandle.Id)) {
                    //string outdata = BenchmarkHandle.StandardOutput.ReadLine();
                    //BenchmarkOutputErrorDataReceivedImpl(outdata);
                    // terminate process situations
                    if (_benchmarkTimer.Elapsed.Seconds >= (benchmarkTimeWait + 2)
                        || BenchmarkSignalQuit
                        || BenchmarkSignalFinnished
                        || BenchmarkSignalHanged
                        || BenchmarkSignalTimedout
                        || BenchmarkException != null) {
                        // maybe will have to KILL process
                        KillClaymoreMinerBase(MinerExeName);
                        if (BenchmarkSignalTimedout) {
                            throw new Exception("Benchmark timedout");
                        }
                        if (BenchmarkException != null) {
                            throw BenchmarkException;
                        }
                        if (BenchmarkSignalQuit) {
                            throw new Exception("Termined by user request");
                        }
                        if (BenchmarkSignalFinnished) {
                            break;
                        }
                        keepRunning = false;
                        break;
                    } else {
                        // wait a second reduce CPU load
                        Thread.Sleep(1000);
                    }

                }
            } catch (Exception ex) {
                BenchmarkAlgorithm.BenchmarkSpeed = 0;
                Helpers.ConsolePrint(MinerTAG(), "Benchmark Exception: " + ex.Message);
                if (BenchmarkComunicator != null && !OnBenchmarkCompleteCalled) {
                    OnBenchmarkCompleteCalled = true;
                    BenchmarkComunicator.OnBenchmarkComplete(false, BenchmarkSignalTimedout ? International.GetText("Benchmark_Timedout") : International.GetText("Benchmark_Terminated"));
                }
            } finally {
                BenchmarkAlgorithm.BenchmarkSpeed = 0;
                BenchmarkProcessStatus = BenchmarkProcessStatus.Finished;
                // find latest log file
                string latestLogFile = "";
                var dirInfo = new DirectoryInfo(this.WorkingDirectory);
                foreach (var file in dirInfo.GetFiles("*_log.txt")) {
                    latestLogFile = file.Name;
                    break;
                }
                // read file log
                if (File.Exists(WorkingDirectory + latestLogFile)) {
                    var lines = File.ReadAllLines(WorkingDirectory + latestLogFile);
                    foreach (var line in lines) {
                        if (line != null) {
                            string lineLowered = line.ToLower();
                            if (lineLowered.Contains(LOOK_FOR_START)) {
                                benchmark_sum += getNumber(lineLowered);
                                ++benchmark_read_count;
                            }
                        }
                    }
                    if (benchmark_read_count > 0) {
                        BenchmarkAlgorithm.BenchmarkSpeed = benchmark_sum / benchmark_read_count;
                        BenchmarkProcessStatus = BenchmarkProcessStatus.Success;
                    }
                }
                Helpers.ConsolePrint("BENCHMARK", "Final Speed: " + Helpers.FormatSpeedOutput(BenchmarkAlgorithm.BenchmarkSpeed));
                Helpers.ConsolePrint("BENCHMARK", "Benchmark ends");
                if (BenchmarkComunicator != null && !OnBenchmarkCompleteCalled) {
                    OnBenchmarkCompleteCalled = true;
                    BenchmarkComunicator.OnBenchmarkComplete(true, "Success");
                }
            }
        }

        protected void CleanAllOldLogs() {
            // clean old logs
            try {
                var dirInfo = new DirectoryInfo(this.WorkingDirectory);
                var deleteContains = "_log.txt";
                if (dirInfo != null && dirInfo.Exists) {
                    foreach (FileInfo file in dirInfo.GetFiles()) {
                        if (file.Name.Contains(deleteContains)) {
                            file.Delete();
                        }
                    }
                }
            } catch { }
        }

        // stub benchmarks read from file
        protected override void BenchmarkOutputErrorDataReceivedImpl(string outdata) {
            CheckOutdata(outdata);
        }

        protected override bool BenchmarkParseLine(string outdata) {
            Helpers.ConsolePrint("BENCHMARK", outdata);
            return false;
        }

        protected double getNumber(string outdata) {
            return getNumber(outdata, LOOK_FOR_START, LOOK_FOR_END);
        }

        protected double getNumber(string outdata, string LOOK_FOR_START, string LOOK_FOR_END) {
            try {
                int speedStart = outdata.IndexOf(LOOK_FOR_START);
                string speed = outdata.Substring(speedStart, outdata.Length - speedStart);
                speed = speed.Replace(LOOK_FOR_START, "");
                speed = speed.Substring(0, speed.IndexOf(LOOK_FOR_END));
                speed = speed.Trim();
                return Double.Parse(speed, CultureInfo.InvariantCulture);
            } catch (Exception ex) {
                Helpers.ConsolePrint("getNumber", ex.Message + " | args => " + outdata + " | " + LOOK_FOR_END + " | " + LOOK_FOR_START);
            }
            return 0;
        }


    }
}