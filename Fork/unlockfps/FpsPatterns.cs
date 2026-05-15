using UnlockFps.Logging;
using UnlockFps.Utils;
using System.IO;
using System;

namespace UnlockFps;

internal static class FpsPatterns
{
    private static readonly ILogger Logger = LogManager.GetLogger(nameof(FpsPatterns));
    private const uint DontResolveDllReferences = 0x01;
    private const string Il2CppSectionName = "il2cpp";
    private const string FpsPattern = "B9 3C 00 00 00 E8";

    public static unsafe nint ProvideAddress(NativeModuleInfo mainModule)
    {
        var imageBytes = MapPEFile(mainModule.FilePath);

        fixed (byte* pImage = imageBytes)
        {
            nint mappedMainModule = (nint)pImage;

            if (!ProcessUtils.TryGetSection(mappedMainModule, Il2CppSectionName, out var il2cppSection))
            {
                throw new InvalidOperationException(
                    $"Failed to find '{Il2CppSectionName}' section in {mainModule.FilePath}.");
            }

            var candidates = ProcessUtils.PatternScanAll(il2cppSection, FpsPattern);
            Logger.LogDebug($"Found {candidates.Count} FPS pattern candidate(s) in {Il2CppSectionName}.");

            foreach (var candidateAddress in candidates)
            {
                var localFpsAddress = TryResolveLocalFpsAddress((byte*)candidateAddress);
                if (localFpsAddress == null)
                    continue;

                var localImageBase = (byte*)mappedMainModule;
                var remoteImageBase = (byte*)mainModule.BaseAddress;
                var remoteFpsAddress = remoteImageBase + (localFpsAddress - localImageBase);

                Logger.LogDebug(
                    $"Resolved FPS address: local=0x{(nint)localFpsAddress:X16}, remote=0x{(nint)remoteFpsAddress:X16}");
                return (nint)remoteFpsAddress;
            }
        }

        throw new InvalidOperationException("Unrecognized FPS pattern.");
    }

    private static byte[] MapPEFile(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fs);

        fs.Position = 0x3C;
        var peOffset = reader.ReadInt32();

        fs.Position = peOffset;
        var signature = reader.ReadUInt32();
        if (signature != 0x00004550) throw new InvalidOperationException("Invalid PE signature");

        var machine = reader.ReadUInt16();
        var numberOfSections = reader.ReadUInt16();
        fs.Position += 12;
        var sizeOfOptionalHeader = reader.ReadUInt16();
        fs.Position += 2;

        var optionalHeaderOffset = fs.Position;
        fs.Position = optionalHeaderOffset + 56;
        var sizeOfImage = reader.ReadUInt32();
        var sizeOfHeaders = reader.ReadUInt32();

        var imageBytes = new byte[sizeOfImage];

        fs.Position = 0;
        var bytesToRead = (int)Math.Min(sizeOfHeaders, fs.Length);
        if (bytesToRead > 0)
        {
            var headerBytes = reader.ReadBytes(bytesToRead);
            Array.Copy(headerBytes, imageBytes, headerBytes.Length);
        }

        var sectionHeadersOffset = optionalHeaderOffset + sizeOfOptionalHeader;

        for (int i = 0; i < numberOfSections; i++)
        {
            fs.Position = sectionHeadersOffset + i * 40;
            var nameBytes = reader.ReadBytes(8);
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var sizeOfRawData = reader.ReadUInt32();
            var pointerToRawData = reader.ReadUInt32();

            var sizeToRead = Math.Min(virtualSize == 0 ? sizeOfRawData : virtualSize, sizeOfRawData);
            if (sizeToRead > 0 && pointerToRawData > 0 && pointerToRawData < fs.Length)
            {
                sizeToRead = Math.Min(sizeToRead, (uint)(fs.Length - pointerToRawData));
                fs.Position = pointerToRawData;
                var sectionData = reader.ReadBytes((int)sizeToRead);
                Array.Copy(sectionData, 0, imageBytes, (int)virtualAddress, sectionData.Length);
            }
        }

        return imageBytes;
    }

    private static unsafe byte* TryResolveLocalFpsAddress(byte* candidate)
    {
        var branch = candidate + 5;
        if (!IsExpectedBranchChain(branch))
            return null;

        while (branch[0] is 0xE8 or 0xE9)
        {
            branch = FollowBranch(branch);
        }

        return ResolveRipRelativeAddress(branch, displacementOffset: 2, instructionSize: 6);
    }

    private static unsafe bool IsExpectedBranchChain(byte* branch)
    {
        if (branch[0] != 0xE8)
            return false;

        var firstTarget = FollowBranch(branch);
        return firstTarget[0] == 0xE9;
    }

    private static unsafe byte* FollowBranch(byte* instruction)
    {
        var displacement = *(int*)(instruction + 1);
        return instruction + displacement + 5;
    }

    private static unsafe byte* ResolveRipRelativeAddress(byte* instruction, int displacementOffset,
        int instructionSize)
    {
        var displacement = *(int*)(instruction + displacementOffset);
        return instruction + displacement + instructionSize;
    }
}
