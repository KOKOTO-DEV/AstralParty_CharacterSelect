using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AstralPartyHeroGroups;

internal sealed unsafe partial class PanelObserver
{
    private readonly Dictionary<int, AsyncOperationHandle<Texture2D>> pendingPortraits = new();
    private readonly Dictionary<int, (IntPtr Texture, nint GcHandle, AsyncOperationHandle<Texture2D> Asset)> mapPortraits = new();
    private readonly HashSet<int> failedPortraits = new();
    private IntPtr mapPortraitLoaderClass;
    private IntPtr mapPortraitTextureClass;

    private void RequestMapPortraits(IEnumerable<int> ids)
    {
        if (mapPortraitLoaderClass == IntPtr.Zero) mapPortraitLoaderClass = FindFairyType("GLoader");
        if (mapPortraitTextureClass == IntPtr.Zero) mapPortraitTextureClass = FindFairyType("NTexture");
        if (mapPortraitLoaderClass == IntPtr.Zero || mapPortraitTextureClass == IntPtr.Zero) return;
        foreach (var id in ids)
        {
            if (pendingPortraits.ContainsKey(id) || mapPortraits.ContainsKey(id) || failedPortraits.Contains(id))
                continue;
            try
            {
                var key = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp($"UT_Item_Hero_{id}"));
                pendingPortraits.Add(id, Addressables.LoadAssetAsync<Texture2D>(key));
            }
            catch (Exception error)
            {
                failedPortraits.Add(id);
                logger.LogDebug($"Map portrait {id}: {error.Message}");
            }
        }
    }

    private bool PollMapPortraits()
    {
        var updated = false;
        foreach (var id in new List<int>(pendingPortraits.Keys))
        {
            var asset = pendingPortraits[id];
            if (!asset.IsDone) continue;
            pendingPortraits.Remove(id);
            try
            {
                var image = asset.Result;
                if (image == null) throw new InvalidOperationException("Portrait asset unavailable.");
                var constructor = FindMethod(mapPortraitTextureClass, ".ctor", "UnityEngine.Texture");
                var destroyMethod = IL2CPP.il2cpp_class_get_field_from_name(mapPortraitTextureClass, "destroyMethod");
                if (constructor == IntPtr.Zero || destroyMethod == IntPtr.Zero)
                    throw new MissingMethodException("FairyGUI.NTexture");
                var texture = IL2CPP.il2cpp_object_new(mapPortraitTextureClass);
                var handle = IL2CPP.il2cpp_gchandle_new(texture, false);
                try
                {
                    void* pointer = (void*)image.Pointer;
                    Invoke(constructor, texture, &pointer);
                    var keepUnityTexture = 2;
                    IL2CPP.il2cpp_field_set_value(texture, destroyMethod, &keepUnityTexture);
                    mapPortraits.Add(id, (texture, handle, asset));
                    updated = true;
                }
                catch
                {
                    IL2CPP.il2cpp_gchandle_free(handle);
                    throw;
                }
            }
            catch (Exception error)
            {
                failedPortraits.Add(id);
                Addressables.Release(asset);
                logger.LogDebug($"Map portrait {id}: {error.Message}");
            }
        }
        return updated;
    }

    internal void ReleaseMapPortraits()
    {
        foreach (var asset in pendingPortraits.Values) Addressables.Release(asset);
        pendingPortraits.Clear();
        foreach (var portrait in mapPortraits.Values)
        {
            try { Call(portrait.Texture, "Dispose"); }
            finally
            {
                IL2CPP.il2cpp_gchandle_free(portrait.GcHandle);
                Addressables.Release(portrait.Asset);
            }
        }
        mapPortraits.Clear();
    }
}
