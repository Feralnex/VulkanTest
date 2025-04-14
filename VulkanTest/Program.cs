using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace VulkanTest;

public unsafe class Program
{
    public readonly bool DebugUtils;
    public VkInstance VkInstance;

    private readonly VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
    public VkPhysicalDevice PhysicalDevice;
    public readonly VkDevice VkDevice;
    public readonly VkQueue GraphicsQueue;
    public readonly VkQueue PresentQueue;

    public static void Main(string[] args)
    {
        var app = new Program();
        app.Initialize();
        app.Dispose();
    }

    public void Initialize()
    {
        vkInitialize();
        var pEngineName = new VkUtf8ReadOnlyString("Stride"u8);
        var applicationInfo = new VkApplicationInfo
        {
            pEngineName = pEngineName,
            //engineVersion = new VkVersion()
        };

        var validationLayerNames = stackalloc VkUtf8String[]
        {
                VK_LAYER_KHRONOS_VALIDATION_EXTENSION_NAME,
            };
        var validationLayers = new Span<VkUtf8String>(validationLayerNames, 1);
        var enabledLayerNames = new List<VkUtf8String>();

        var layers = vkEnumerateInstanceLayerProperties();

        for (int index = 0; index < layers.Length; index++)
        {
            var props = layers[index];
            var name = new VkUtf8String(props.layerName);
            var indexOfLayerName = validationLayers.IndexOf(name);

            if (indexOfLayerName >= 0)
                enabledLayerNames.Add(validationLayerNames[indexOfLayerName]);
        }

        var supportedExtensionNames = stackalloc VkUtf8String[]
        {
                VK_KHR_SURFACE_EXTENSION_NAME,
                VK_KHR_WIN32_SURFACE_EXTENSION_NAME,
                VK_KHR_ANDROID_SURFACE_EXTENSION_NAME,
                VK_KHR_XLIB_SURFACE_EXTENSION_NAME,
                VK_KHR_XCB_SURFACE_EXTENSION_NAME,
                VK_EXT_DEBUG_UTILS_EXTENSION_NAME
            };
        var supportedExtensions = new Span<VkUtf8String>(supportedExtensionNames, 6);
        var availableExtensionNames = GetAvailableExtensionNames(supportedExtensions);
        ValidateSurfaceExtensionNamesAvailability(availableExtensionNames);
        var desiredExtensionNames = new HashSet<VkUtf8String>
            {
                VK_KHR_SURFACE_EXTENSION_NAME,
                GetPlatformRelatedSurfaceExtensionName(availableExtensionNames)
            };


        bool enableDebugReport = availableExtensionNames.Contains(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
        if (enableDebugReport)
            desiredExtensionNames.Add(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);

        using VkStringArray ppEnabledLayerNames = new(enabledLayerNames);
        using VkStringArray ppEnabledExtensionNames = new(desiredExtensionNames);

        var instanceCreateInfo = new VkInstanceCreateInfo
        {
            sType = VkStructureType.InstanceCreateInfo,
            pApplicationInfo = &applicationInfo,
            enabledLayerCount = ppEnabledLayerNames.Length,
            ppEnabledLayerNames = ppEnabledLayerNames,
            enabledExtensionCount = ppEnabledExtensionNames.Length,
            ppEnabledExtensionNames = ppEnabledExtensionNames,
        };

        VkResult result = vkCreateInstance(&instanceCreateInfo, null, out VkInstance);
        if (result != VkResult.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan instance: {result}");
        }

        vkLoadInstance(VkInstance);

        // Find physical device, setup queue family, and create device.
        var nativePhysicalDevices = vkEnumeratePhysicalDevices(VkInstance);
        if (nativePhysicalDevices.Length == 0)
        {
            throw new Exception("Vulkan: Failed to find GPUs with Vulkan support");
        }

        int selectedQueueFamilyIndex = -1;
        uint queueFamilyCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(nativePhysicalDevices[0], &queueFamilyCount, null);
        if (queueFamilyCount == 0)
        {
            throw new Exception("No queue families found.");
        }

        VkQueueFamilyProperties* queueFamilies = stackalloc VkQueueFamilyProperties[(int)queueFamilyCount];
        vkGetPhysicalDeviceQueueFamilyProperties(nativePhysicalDevices[0], &queueFamilyCount, queueFamilies);

        for (int i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].queueFlags & VkQueueFlags.Graphics) != 0)
            {
                selectedQueueFamilyIndex = i;
                break;
            }
        }

        if (selectedQueueFamilyIndex == -1)
        {
            throw new Exception("No suitable queue family found.");
        }

        PhysicalDevice = nativePhysicalDevices[0];

        float queuePriorities = 0;
        var queueCreateInfo = new VkDeviceQueueCreateInfo
        {
            sType = VkStructureType.DeviceQueueCreateInfo,
            queueFamilyIndex = 0,
            queueCount = 1,
            pQueuePriorities = &queuePriorities,
        };

        var enabledFeature = new VkPhysicalDeviceFeatures
        {
            fillModeNonSolid = true,
            shaderClipDistance = true,
            shaderCullDistance = true,
            samplerAnisotropy = true,
            depthClamp = true,
        };

        var supportedExtensionProperties = stackalloc VkUtf8String[]
        {
                VK_KHR_SWAPCHAIN_EXTENSION_NAME,
                VK_EXT_DEBUG_MARKER_EXTENSION_NAME,
            };
        var supportedProperties = new Span<VkUtf8String>(supportedExtensionProperties, 2);
        var availableExtensionProperties = GetAvailableExtensionProperties(supportedProperties);
        ValidateExtensionPropertiesAvailability(availableExtensionProperties);
        var desiredExtensionProperties = new HashSet<VkUtf8String>
            {
                VK_KHR_SWAPCHAIN_EXTENSION_NAME
            };
        if (availableExtensionProperties.Contains(VK_EXT_DEBUG_MARKER_EXTENSION_NAME))
        {
            desiredExtensionProperties.Add(VK_EXT_DEBUG_MARKER_EXTENSION_NAME);
        }

        using VkStringArray ppEnabledExtensionNames2 = new(desiredExtensionProperties);
        var deviceCreateInfo = new VkDeviceCreateInfo
        {
            sType = VkStructureType.DeviceCreateInfo,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueCreateInfo,
            enabledExtensionCount = ppEnabledExtensionNames2.Length,
            ppEnabledExtensionNames = ppEnabledExtensionNames2,
            pEnabledFeatures = &enabledFeature,
        };

        VkResult deviceResult = vkCreateDevice(PhysicalDevice, in deviceCreateInfo, null, out var nativeDevice);
        if (deviceResult != VkResult.Success)
        {
            throw new Exception($"Failed to create Vulkan device: {deviceResult}");
        }

        VkQueue commandQueue;
        vkGetDeviceQueue(nativeDevice, (uint)selectedQueueFamilyIndex, 0, &commandQueue);
        if (commandQueue == VkQueue.Null)
        {
            throw new Exception("Failed to retrieve Vulkan device queue.");
        }

        vkGetPhysicalDeviceProperties(PhysicalDevice, out VkPhysicalDeviceProperties properties);
        var availableDeviceExtensions = vkEnumerateDeviceExtensionProperties(PhysicalDevice);
    }

    private unsafe static HashSet<VkUtf8String> GetAvailableExtensionNames(Span<VkUtf8String> supportedExtensionNames)
    {
        var availableExtensionNames = new HashSet<VkUtf8String>();
        vkEnumerateInstanceExtensionProperties(out uint extensionCount).CheckResult();
        var extensionProperties = new VkExtensionProperties[extensionCount];
        vkEnumerateInstanceExtensionProperties(extensionProperties).CheckResult();

        for (int index = 0; index < extensionCount; index++)
        {
            var extensionProperty = extensionProperties[index];
            var name = new VkUtf8String(extensionProperty.extensionName).Span;
            var indexOfExtensionName = supportedExtensionNames.IndexOf(name);

            if (indexOfExtensionName >= 0)
                availableExtensionNames.Add(supportedExtensionNames[indexOfExtensionName]);
        }

        return availableExtensionNames;
    }

    private static void ValidateSurfaceExtensionNamesAvailability(HashSet<VkUtf8String> availableExtensionNames)
    {
        if (!availableExtensionNames.Contains(VK_KHR_SURFACE_EXTENSION_NAME))
            throw new InvalidOperationException($"Required extension {Encoding.UTF8.GetString(VK_KHR_SURFACE_EXTENSION_NAME)} is not available");

        if (!availableExtensionNames.Contains(VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
            throw new InvalidOperationException($"Required extension {Encoding.UTF8.GetString(VK_KHR_WIN32_SURFACE_EXTENSION_NAME)} is not available");
    }

    private static VkUtf8String GetPlatformRelatedSurfaceExtensionName(HashSet<VkUtf8String> availableExtensionNames)
    {
        VkUtf8String surfaceExtensionName = VK_KHR_SURFACE_EXTENSION_NAME;
        surfaceExtensionName = VK_KHR_WIN32_SURFACE_EXTENSION_NAME;

        return surfaceExtensionName;
    }

    private unsafe HashSet<VkUtf8String> GetAvailableExtensionProperties(Span<VkUtf8String> supportedExtensionProperties)
    {
        var availableExtensionProperties = new HashSet<VkUtf8String>();
        var extensionProperties = vkEnumerateDeviceExtensionProperties(PhysicalDevice);

        fixed (VkExtensionProperties* extensionPropertiesPtr = extensionProperties)
        {
            for (int index = 0; index < extensionProperties.Length; index++)
            {
                var namePointer = extensionPropertiesPtr[index].extensionName;
                var name = new VkUtf8String(namePointer);
                var indexOfExtensionName = supportedExtensionProperties.IndexOf(name);

                if (indexOfExtensionName >= 0)
                    availableExtensionProperties.Add(supportedExtensionProperties[indexOfExtensionName]);
            }
        }

        return availableExtensionProperties;
    }

    private static void ValidateExtensionPropertiesAvailability(HashSet<VkUtf8String> availableExtensionProperties)
    {
        if (!availableExtensionProperties.Contains(VK_KHR_SWAPCHAIN_EXTENSION_NAME))
            throw new InvalidOperationException();
    }

    public void Dispose()
    {
        if (VkDevice.IsNotNull)
        {
            vkDestroyDevice(VkDevice);
        }

        if (VkInstance != VkInstance.Null)
        {
            vkDestroyInstance(VkInstance);
        }
    }
}