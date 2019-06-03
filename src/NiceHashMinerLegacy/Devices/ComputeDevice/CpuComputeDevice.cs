﻿using System;
using System.Diagnostics;
using NiceHashMiner.Utils.Guid;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Device;
using NiceHashMinerLegacy.Common.Enums;

namespace NiceHashMiner.Devices
{
    public class CpuComputeDevice : ComputeDevice
    {
        private readonly PerformanceCounter _cpuCounter;

        public override float Load
        {
            get
            {
                try
                {
                    if (_cpuCounter != null) return _cpuCounter.NextValue();
                }
                catch (Exception e) {
                    Logger.Error("CPUDIAG", e.ToString());
                }
                return -1;
            }
        }

        public CpuComputeDevice(int id, string name, int threads, ulong affinityMask, int cpuCount)
            : base(id,
                name,
                true,
                DeviceType.CPU,
                string.Format(Translations.Tr("CPU#{0}"), cpuCount),
                0)
        {
            Threads = threads;
            AffinityMask = affinityMask;
            //Uuid = GetUuid(ID, GroupNames.GetGroupName(DeviceGroupType, ID), Name, DeviceGroupType);
            Index = ID; // Don't increment for CPU

            var hashedInfo = $"{id}--{name}--{threads}";
            var uuidHEX = UUID.V5(UUID.Nil().AsGuid(), hashedInfo).AsGuid().ToString();
            var Uuid = $"CPU-{uuidHEX}";

            _cpuCounter = new PerformanceCounter
            {
                CategoryName = "Processor",
                CounterName = "% Processor Time",
                InstanceName = "_Total"
            };

            // plugin device
            var bd = new BaseDevice(DeviceType.CPU, Uuid, name, ID); // TODO UUID
            PluginDevice = new CPUDevice(bd, threads, true, affinityMask); // TODO hyperthreading 
        }
    }
}
