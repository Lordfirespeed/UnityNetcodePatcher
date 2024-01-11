﻿using System;
using System.IO;
using System.Reflection.PortableExecutable;
using Microsoft.DiaSymReader.Tools;
using NetcodePatcher.Extensions;
using Serilog;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace NetcodePatcher.CodeGen;

public class CompiledAssemblyFromFile : ICompiledAssembly
{
    readonly string _assemblyPath;

    public string Name => Path.GetFileNameWithoutExtension(_assemblyPath);
    public string[] References { get; set; } = Array.Empty<string>();
    public string[] Defines { get; set; } = Array.Empty<string>();
    public bool DebugSymbolsAreEmbedded { get; private set; } = false;
    public InMemoryAssembly InMemoryAssembly { get; }

    public byte[] ReadPdb(FileStream peStream)
    {
        using var peReader = new PEReader(peStream, PEStreamOptions.LeaveOpen);
        string assemblyName = Path.GetFileNameWithoutExtension(_assemblyPath);
        string pdbPath = $"{assemblyName}.pdb" + ".pdb";
        
        if (File.Exists(pdbPath))
        {
            Log.Information("Found debug info : ({PdbFileName})", Path.GetFileName(pdbPath));
            using var srcPdbStream = new FileStream(pdbPath, FileMode.Open, FileAccess.Read);
            if (!PdbConverter.IsPortable(srcPdbStream))
                throw new ArgumentException("Unsupported debug symbol type - should be 'portable'");
            
            srcPdbStream.Seek(0, SeekOrigin.Begin);
            using var pdbDataStream = new MemoryStream();
            srcPdbStream.CopyTo(pdbDataStream);
            return pdbDataStream.ToArray();
        }
        
        if (peReader.TryOpenAssociatedPortablePdb(_assemblyPath, File.OpenRead, out var pdbReaderProvider, out _))
        {
            var pdbReader = pdbReaderProvider!.GetMetadataReader();
            
            Log.Information("Found embedded debug info : ({AssemblyName})", assemblyName);
            
            DebugSymbolsAreEmbedded = true;
            return pdbReader.ReadAllBytes();
        }

        throw new InvalidDataException($"Failed to discover portable debug information for {Path.GetFileName(_assemblyPath)}");
    }
    
    public CompiledAssemblyFromFile(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
        using var peSrcStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read);

        byte[] pdbData = ReadPdb(peSrcStream);
        
        using var peStream = new MemoryStream();
        peSrcStream.CopyTo(peStream);
        byte[] peData = peStream.ToArray();
        
        peSrcStream.Seek(0, SeekOrigin.Begin);
        InMemoryAssembly = new InMemoryAssembly(peData, pdbData);
    }
}