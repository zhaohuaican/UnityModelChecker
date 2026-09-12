using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/*
 * ModelChecker.cs
 *
 * 作者:阿灿
 * 创建日期: 2023-06-15
 *
 * 描述:
 * 包含所有模型检查相关的逻辑实现
 */
public static class ModelChecker
{
    public static void CountTriangles(MeshRenderer[] meshRenderers, SkinnedMeshRenderer[] skinnedMeshRenderers, 
                                     ref int totalTriangles, ref int totalObjects)
    {
        totalTriangles = 0;
        totalObjects = 0;

        // 遍历静态网格对象
        foreach (var mr in meshRenderers)
        {
            if (mr == null || mr.gameObject == null) continue;

            var mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                totalObjects++;
                totalTriangles += mf.sharedMesh.triangles.Length / 3;
            }
        }

        // 遍历动态网格对象（带骨骼）
        foreach (var smr in skinnedMeshRenderers)
        {
            if (smr == null || smr.sharedMesh == null) continue;

            totalObjects++;
            totalTriangles += smr.sharedMesh.triangles.Length / 3;
        }
    }

    public static void CheckMeshRenderer(MeshRenderer renderer, ModelCheckConfig config, 
                                        List<ModelIssue> issues, HashSet<Texture2D> checkedTextures, 
                                        HashSet<Material> materialSet)
    {
        var meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        var mesh = meshFilter.sharedMesh;
        var gameObject = renderer.gameObject;

        // 添加材质到集合
        foreach (var mat in renderer.sharedMaterials)
        {
            if (mat != null)
                materialSet.Add(mat);
        }

        // 检查各项规范
        CheckTriangleCount(gameObject, mesh, config, issues);
        CheckMaterials(gameObject, renderer.sharedMaterials, config, issues);
        CheckMeshProperties(gameObject, mesh, issues);
        CheckNaming(gameObject, config, issues);
        CheckTransform(gameObject, issues);
        CheckTextures(gameObject, renderer.sharedMaterials, config, issues, checkedTextures);
        CheckUVs(gameObject, mesh, config, issues);
        CheckMeshImportSettings(gameObject, mesh, issues);

        // 检查轴心偏移
        if (config.checkPivotOffset)
        {
            CheckPivotOffset(gameObject, mesh, config, issues);
        }
    }

    public static void CheckSkinnedMeshRenderer(SkinnedMeshRenderer renderer, ModelCheckConfig config, 
                                               List<ModelIssue> issues, HashSet<Texture2D> checkedTextures, 
                                               HashSet<Material> materialSet)
    {
        if (renderer.sharedMesh == null) return;

        var mesh = renderer.sharedMesh;
        var gameObject = renderer.gameObject;

        // 添加材质到集合
        foreach (var mat in renderer.sharedMaterials)
        {
            if (mat != null)
                materialSet.Add(mat);
        }

        // 检查基础规范
        CheckTriangleCount(gameObject, mesh, config, issues);
        CheckMaterials(gameObject, renderer.sharedMaterials, config, issues);
        CheckMeshProperties(gameObject, mesh, issues);
        CheckNaming(gameObject, config, issues);
        CheckTransform(gameObject, issues);
        CheckTextures(gameObject, renderer.sharedMaterials, config, issues, checkedTextures);
        CheckUVs(gameObject, mesh, config, issues);
        CheckMeshImportSettings(gameObject, mesh, issues);

        // 检查轴心偏移
        if (config.checkPivotOffset)
        {
            CheckPivotOffset(gameObject, mesh, config, issues);
        }

        // 检查骨骼相关
        CheckBones(gameObject, renderer, config, issues);
        CheckAnimation(gameObject, issues);
    }

    public static void CheckPivotOffset(GameObject obj, Mesh mesh, ModelCheckConfig config, List<ModelIssue> issues)
    {
        if (mesh.vertices.Length == 0) return;

        // 计算网格的边界框
        Bounds bounds = mesh.bounds;

        // 轴心点在本地坐标系中是(0,0,0)
        Vector3 pivotPoint = Vector3.zero;

        // 计算轴心点到边界框的距离
        Vector3 closestPoint = bounds.ClosestPoint(pivotPoint);
        Vector3 offset = pivotPoint - closestPoint;
        float distanceToBounds = offset.magnitude;

        // 如果轴心点超出边界框的容差范围，记录问题
        if (distanceToBounds > config.pivotTolerance)
        {
            // 计算几何中心用于显示信息
            Vector3 geometricCenter = Vector3.zero;
            foreach (var vertex in mesh.vertices)
            {
                geometricCenter += vertex;
            }

            geometricCenter /= mesh.vertices.Length;

            var pivotInfo = new PivotOffsetInfo
            {
                position = obj.transform.position,
                offset = offset,
                distance = distanceToBounds
            };

            var issue = new ModelIssue
            {
                gameObject = obj,
                modelName = obj.name,
                type = IssueType.PivotOffset,
                description = $"轴心点超出网格边界，距离边界: {distanceToBounds:F2} 米(容差: {config.pivotTolerance:F2}米)",
                suggestion = "在建模软件中将轴心点移动到模型网格内部",
                pivotOffsetInfo = pivotInfo
            };

            // 找到插入位置，保持距离从大到小的顺序
            int insertIndex = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].type == IssueType.PivotOffset &&
                    issues[i].pivotOffsetInfo.distance > distanceToBounds)
                {
                    insertIndex = i + 1;
                }
                else if (issues[i].type == IssueType.PivotOffset)
                {
                    break;
                }
            }

            issues.Insert(insertIndex, issue);
        }
    }

    public static void CheckTriangleCount(GameObject obj, Mesh mesh, ModelCheckConfig config, List<ModelIssue> issues)
    {
        int triangleCount = mesh.triangles.Length / 3;
        if (triangleCount > config.maxTriangles)
        {
            AddIssue(obj, IssueType.TriangleCount,
                $"三角面数过多: {triangleCount} (建议: <{config.maxTriangles})",
                "使用建模软件减少面数，删除不必要的细节，优化模型结构", issues);
        }
    }

    public static void CheckMaterials(GameObject obj, Material[] materials, ModelCheckConfig config, List<ModelIssue> issues)
    {
        // 收集所有材质相关问题
        List<(ModelIssue issue, int materialLength)> materialIssues = new List<(ModelIssue, int)>();

        // 检查材质数量
        if (materials.Length > config.maxMaterials)
        {
            var issue = new ModelIssue
            {
                gameObject = obj,
                modelName = obj.name,
                type = IssueType.MaterialCount,
                description = $"材质数量过多: {materials.Length} (建议: <={config.maxMaterials})",
                suggestion = "合并材质，使用贴图"
            };
            materialIssues.Add((issue, materials.Length));
        }

        // 检查材质球引用
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null)
            {
                var issue = new ModelIssue
                {
                    gameObject = obj,
                    modelName = obj.name,
                    type = IssueType.MissingMaterial,
                    description = $"材质球引用丢失: 索引 {i}",
                    suggestion = "重新指定正确的材质球"
                };
                materialIssues.Add((issue, materials.Length));
            }
        }

        // 按材质数量从大到小排序后添加到issues
        foreach (var (issue, materialLength) in materialIssues.OrderByDescending(x => x.materialLength))
        {
            // 找到插入位置，保持全局按材质数量排序
            int insertIndex = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if ((issues[i].type == IssueType.MaterialCount || issues[i].type == IssueType.MissingMaterial))
                {
                    // 获取该问题对应的材质数量 - 使用sharedMaterials避免创建实例
                    var existingObj = issues[i].gameObject;
                    var renderer = existingObj.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterials.Length < materialLength)
                    {
                        break;
                    }

                    insertIndex = i + 1;
                }
            }

            issues.Insert(insertIndex, issue);
        }
    }

    public static void CheckMeshProperties(GameObject obj, Mesh mesh, List<ModelIssue> issues)
    {
        // 检查是否有非流形边等拓扑问题
        if (mesh.vertices.Length == 0)
        {
            AddIssue(obj, IssueType.MeshTopology,
                "网格没有顶点数据",
                "重新导出模型，确保网格数据完整", issues);
        }

        // 检查是否为四边面
        if (mesh.triangles.Length % 3 != 0)
        {
            AddIssue(obj, IssueType.MeshTopology,
                "网格可能包含非三角面",
                "导出前进行三角化处理", issues);
        }
    }

    public static void CheckNaming(GameObject obj, ModelCheckConfig config, List<ModelIssue> issues)
    {
        // 检查默认命名
        if (obj.name.Contains("pCube") || obj.name.Contains("Cube") ||
            obj.name.Contains("default") || obj.name.Contains("GameObject")|| 
            obj.name.Contains("polySurface")|| obj.name.Contains("pasted")|| 
            obj.name.Contains("pCylinder")||obj.name.Contains("pSphere")||obj.name.Contains("pCone")
            ||obj.name.Contains("pTorus")||obj.name.Contains("pPlane")||obj.name.Contains("pDisc"))
        {
            AddIssue(obj, IssueType.Naming,
                $"使用默认命名: {obj.name}",
                "使用有意义的命名，如 静态物体：SM_模型名称_01，动态物体：DM_模型名称_02", issues);
        }

        // 检查子节点命名
        if (config.checkChildrenNaming)
        {
            CheckChildrenNaming(obj, issues);
        }
    }
    // public static void CheckNaming(GameObject obj, ModelCheckConfig config, List<ModelIssue> issues)
    // {
    //     string name = obj.name;
    //
    //     // 检查是否为默认命名
    //     string[] defaultNameKeywords = {
    //         "pCube", "Cube", "default", "GameObject", "polySurface", "pasted",
    //         "pCylinder", "pSphere", "pCone", "pTorus", "pPlane", "pDisc", "pSuperShape"
    //     };
    //
    //     foreach (var keyword in defaultNameKeywords)
    //     {
    //         if (name.Contains(keyword))
    //         {
    //             AddIssue(obj, IssueType.Naming,
    //                 $"使用默认命名: {name}",
    //                 "使用有意义的命名,如 SM_烧杯_01", issues);
    //             break;
    //         }
    //     }
    //
    //     // 判断是否为无效命名
    //     if (IsInvalidName(name))
    //     {
    //         AddIssue(obj, IssueType.Naming,
    //             $"命名可能无效: {name}",
    //             "请使用有意义的命名，如 SM_烧杯_01，避免使用无意义的缩写或随机字符", issues);
    //     }
    //
    //     // 检查子节点命名
    //     if (config.checkChildrenNaming)
    //     {
    //         CheckChildrenNaming(obj, issues);
    //     }
    // }
    // static bool IsInvalidName(string name)
    // {
    //     // 1. 太短
    //     if (name.Length < 3)
    //         return true;
    //
    //     // 2. 纯数字
    //     if (Regex.IsMatch(name, @"^\d+$"))
    //         return true;
    //
    //     // 3. 包含特殊字符（除下划线）
    //     if (Regex.IsMatch(name, @"[^a-zA-Z0-9_\u4e00-\u9fa5]"))
    //         return true;
    //
    //     // 4. 常见无意义组合
    //     string[] badWords = { "asd", "qwe", "zzz", "test", "temp", "aaa", "new", "model", "obj", "test", "junk" };
    //     string lower = name.ToLower();
    //     foreach (var bad in badWords)
    //     {
    //         if (lower.Contains(bad))
    //             return true;
    //     }
    //
    //     return false;
    // }


    
    private static void CheckChildrenNaming(GameObject obj, List<ModelIssue> issues)
    {
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            if (child.name.Contains("pCube") || child.name.Contains("default"))
            {
                AddIssue(obj, IssueType.Naming,
                    $"子节点使用默认命名: {child.name}",
                    "为子节点使用有意义的命名，如 SM_烧杯_01", issues);
            }

            // 递归检查
            CheckChildrenNaming(child.gameObject, issues);
        }
    }

    public static void CheckTransform(GameObject obj, List<ModelIssue> issues)
    {
        var transform = obj.transform;

        // 检查缩放
        if (transform.localScale != Vector3.one)
        {
            AddIssue(obj, IssueType.Transform,
                $"缩放不为1: {transform.localScale}",
                "导出前将模型缩放重置为1（冻结变换）", issues);
        }

        // 检查空父节点
        CheckEmptyParents(obj, issues);
    }

    private static void CheckEmptyParents(GameObject obj, List<ModelIssue> issues)
    {
        if (obj.transform.childCount == 0) return;

        var components = obj.GetComponents<Component>();
        // 只有Transform组件的空节点
        if (components.Length == 1 && components[0] is Transform)
        {
            AddIssue(obj, IssueType.EmptyNode,
                "发现空节点（只有Transform组件）",
                "删除不必要的空节点，简化层级结构", issues);
        }
    }

    public static void CheckTextures(GameObject obj, Material[] materials, ModelCheckConfig config, 
                                    List<ModelIssue> issues, HashSet<Texture2D> checkedTextures)
    {
        foreach (var material in materials)
        {
            if (material == null) continue;

            // 检查主贴图
            CheckSingleTexture(obj, material.mainTexture as Texture2D, "主贴图", config, issues, checkedTextures);

            // 检查法线贴图
            if (material.HasProperty("_BumpMap"))
            {
                var normalMap = material.GetTexture("_BumpMap") as Texture2D;
                CheckSingleTexture(obj, normalMap, "法线贴图", config, issues, checkedTextures);
                CheckNormalMapSettings(obj, normalMap, issues, checkedTextures);
            }
        }
    }

    private static void CheckSingleTexture(GameObject obj, Texture2D texture, string textureName, 
                                         ModelCheckConfig config, List<ModelIssue> issues, 
                                         HashSet<Texture2D> checkedTextures)
    {
        if (texture == null) return;

        // 如果这个贴图已经检查过了，就跳过
        if (checkedTextures.Contains(texture)) return;

        // 将贴图添加到已检查列表
        checkedTextures.Add(texture);

        // 检查贴图尺寸
        if (texture.width > config.maxTextureSize || texture.height > config.maxTextureSize)
        {
            AddIssue(obj, IssueType.TextureSize,
                $"{textureName}尺寸过大: {texture.width}x{texture.height} (建议: <={config.maxTextureSize})",
                "使用图像软件减小贴图尺寸，推荐1024或2048", issues);
        }

        // 检查是否为2的幂
        if (!IsPowerOfTwo(texture.width) || !IsPowerOfTwo(texture.height))
        {
            AddIssue(obj, IssueType.TexturePowerOfTwo,
                $"{textureName}尺寸不是2的幂: {texture.width}x{texture.height}",
                "调整贴图尺寸，如512、1024、2048等", issues);
        }
    }

    private static void CheckNormalMapSettings(GameObject obj, Texture2D normalMap, 
                                             List<ModelIssue> issues, HashSet<Texture2D> checkedTextures)
    {
        if (normalMap == null) return;

        // 如果这个法线贴图已经检查过了，就跳过
        if (checkedTextures.Contains(normalMap)) return;

        string path = AssetDatabase.GetAssetPath(normalMap);
        if (string.IsNullOrEmpty(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            AddIssue(obj, IssueType.TextureImportSettings,
                "法线贴图未正确设置为NormalMap类型",
                "在贴图导入设置中将Texture Type设置为Normal Map", issues);
        }
    }

    public static void CheckUVs(GameObject obj, Mesh mesh, ModelCheckConfig config, List<ModelIssue> issues)
    {
        // 检查UV0
        if (mesh.uv.Length == 0)
        {
            AddIssue(obj, IssueType.UVMapping,
                "缺少UV0坐标",
                "在建模软件中创建UV映射", issues);
            return; // 没有UV就不需要继续检查了
        }

        // 检查Lightmap UV
        if (config.checkLightmapUV && mesh.uv2.Length == 0)
        {
            AddIssue(obj, IssueType.LightmapUV,
                "缺少Lightmap UV (UV2)",
                "在模型导入设置中启用Generate Lightmap UVs", issues);
        }

        // 检查UV重叠（简化检查）
        if (config.checkUVOverlap)
        {
            CheckUVOverlap(obj, mesh, issues);
        }

        // 检查UV拉伸问题
        if (config.checkUVStretch)
        {
            CheckUVStretch(obj, mesh, config, issues);
        }
    }

    private static void CheckUVOverlap(GameObject obj, Mesh mesh, List<ModelIssue> issues)
    {
        var uvs = mesh.uv;
        if (uvs.Length == 0) return;

        // 检查UV是否超出0-1范围
        bool hasOutOfRangeUV = false;
        foreach (var uv in uvs)
        {
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
            {
                hasOutOfRangeUV = true;
                break;
            }
        }

        if (hasOutOfRangeUV)
        {
            AddIssue(obj, IssueType.UVMapping,
                "UV坐标超出0-1范围",
                "在建模软件中调整UV布局，确保在0-1范围内", issues);
        }
    }

    private static void CheckUVStretch(GameObject obj, Mesh mesh, ModelCheckConfig config, List<ModelIssue> issues)
    {
        var transform = obj.transform;
        float maxStretch = CalculateMaxUVStretch(mesh, transform.localToWorldMatrix);

        // 检查UV拉伸比
        if (maxStretch > config.uvStretchThreshold)
        {
            var renderer = obj.GetComponent<MeshRenderer>();
            var tex = renderer?.sharedMaterial?.mainTexture;
            float texelDensity = EstimateTexelDensity(mesh, transform.localToWorldMatrix, tex);

            string suggestion = GetUVStretchSuggestion(maxStretch, texelDensity);

            var issue = new ModelIssue
            {
                gameObject = obj,
                modelName = obj.name,
                type = IssueType.UVStretch,
                description = $"UV拉伸严重 (拉伸比: {maxStretch:F2}, 密度: {texelDensity:F1} px/m?)",
                suggestion = suggestion,
                uvStretchInfo = new ModelIssue.UVStretchInfo
                {
                    maxStretch = maxStretch,
                    texelDensity = texelDensity
                }
            };

            // 找到插入位置，保持拉伸比从大到小的顺序
            int insertIndex = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].type == IssueType.UVStretch &&
                    issues[i].uvStretchInfo.maxStretch > maxStretch)
                {
                    insertIndex = i + 1;
                }
                else if (issues[i].type == IssueType.UVStretch)
                {
                    break;
                }
            }

            issues.Insert(insertIndex, issue);
        }
    }

    private static float CalculateMaxUVStretch(Mesh mesh, Matrix4x4 localToWorld)
    {
        var vertices = mesh.vertices;
        var uvs = mesh.uv;
        var triangles = mesh.triangles;
        float maxRatio = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];

            Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
            Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
            Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
            float area3D = TriangleArea(p0, p1, p2);
            
            // 计算UV空间中的三角形面积
            Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
            float areaUV = TriangleArea(uv0, uv1, uv2);

            if (areaUV < 1e-6f || area3D < 1e-6f) continue;
            float stretchRatio = Mathf.Max(area3D / areaUV, areaUV / area3D);
            maxRatio = Mathf.Max(maxRatio, stretchRatio);
        }

        return maxRatio;
    }

    private static float EstimateTexelDensity(Mesh mesh, Matrix4x4 localToWorld, Texture texture)
    {
        if (texture == null) return 0f;

        int texWidth = texture.width;
        int texHeight = texture.height;
        var vertices = mesh.vertices;
        var uvs = mesh.uv;
        var triangles = mesh.triangles;

        float totalPixelArea = 0f;
        float totalWorldArea = 0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];

            Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
            Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
            Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
            float worldArea = TriangleArea(p0, p1, p2);

            Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];
            float uvArea = TriangleArea(uv0, uv1, uv2);
            float pixelArea = uvArea * texWidth * texHeight;

            totalPixelArea += pixelArea;
            totalWorldArea += worldArea;
        }

        return totalWorldArea > 0 ? totalPixelArea / totalWorldArea : 0;
    }

    private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return Mathf.Abs(Vector3.Cross(b - a, c - a).z) * 0.5f;
    }

    private static string GetUVStretchSuggestion(float stretch, float density)
    {
        if (stretch > 10f)
            return "UV拉伸严重，建议重新展开UV";
        else if (density < 50f)
            return "贴图密度过低，建议细分UV或提高清晰度";
        else if (density > 1000f)
            return "贴图密度过高，建议降低贴图分辨率";
        return "UV拉伸在可接受范围内";
    }

    public static void CheckMeshImportSettings(GameObject obj, Mesh mesh, List<ModelIssue> issues)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path)) return;

        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        // 检查Read/Write Enable
        if (importer.isReadable)
        {
            AddIssue(obj, IssueType.ImportSettings,
                "模型启用了Read/Write Enable",
                "如非必要，关闭Read/Write Enable以节省内存", issues);
        }
    }

    public static void CheckBones(GameObject obj, SkinnedMeshRenderer renderer, ModelCheckConfig config, List<ModelIssue> issues)
    {
        if (renderer.bones.Length > config.maxBones)
        {
            AddIssue(obj, IssueType.BoneCount,
                $"骨骼数量过多: {renderer.bones.Length} (建议: <={config.maxBones})",
                "优化骨骼结构，删除不必要的骨骼", issues);
        }

        // 检查孤立骨骼
        foreach (var bone in renderer.bones)
        {
            if (bone == null)
            {
                AddIssue(obj, IssueType.MissingBone,
                    "发现丢失的骨骼引用",
                    "重新绑定骨骼或重新导入模型", issues);
            }
        }
    }

    public static void CheckAnimation(GameObject obj, List<ModelIssue> issues)
    {
        var animator = obj.GetComponent<Animator>();
        if (animator == null) return;
    }

    public static void CheckMaterialUsage(Renderer[] renderers, MeshFilter[] meshFilters, ModelCheckConfig config, 
                                         List<ModelIssue> issues, HashSet<Material> materialSet,
                                         ref Dictionary<Material, int> materialUsageStats, 
                                         ref int totalMaterialAssignments, ref float averageTrianglesPerMaterial)
    {
        int totalTriangleCount = 0;
        totalMaterialAssignments = 0;

        // 统计材质使用情况
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            totalMaterialAssignments += mats.Length;
            foreach (var mat in mats)
            {
                if (mat != null)
                {
                    materialSet.Add(mat);
                    // 统计每个材质的使用次数
                    if (materialUsageStats.ContainsKey(mat))
                        materialUsageStats[mat]++;
                    else
                        materialUsageStats[mat] = 1;
                }
            }
        }

        // 统计三角面数
        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            if (mesh != null)
                totalTriangleCount += mesh.triangles.Length / 3;
        }

        int materialCount = materialSet.Count;

        // 核心计算逻辑：结合面数和材质数量得出场景平均每材质球负责的面数
        averageTrianglesPerMaterial = materialCount > 0 ? (float)totalTriangleCount / materialCount : 0f;

        // 评估推荐材质数量范围（估算DrawCall预算）
        float minTrianglesPerDC = totalTriangleCount / (float)config.maxDrawCallBudget;
        float maxTrianglesPerDC = totalTriangleCount / (float)config.minDrawCallBudget;

        Debug.Log("=== 材质使用效率分析 ===");
        Debug.Log("场景三角面总数: " + totalTriangleCount);
        Debug.Log("场景材质球总数: " + materialCount);
        Debug.Log("总材质球指派次数（Renderer使用次数）: " + totalMaterialAssignments);
        Debug.Log("平均每个材质负责面数: " + averageTrianglesPerMaterial.ToString("F1"));
        Debug.Log("若保持" + config.minDrawCallBudget + "个DrawCall，每个DrawCall应负责 ≥ " +
                  Mathf.RoundToInt(minTrianglesPerDC) + " 面");
        Debug.Log("若最大允许" + config.maxDrawCallBudget + "个DrawCall，每个DrawCall至少负责 ≤ " +
                  Mathf.RoundToInt(maxTrianglesPerDC) + " 面");

        // 添加问题检测
        if (averageTrianglesPerMaterial < minTrianglesPerDC)
        {
            Debug.LogWarning("平均每材质负责面数偏低，可能造成材质球使用过多、DrawCall超限，建议合并材质/合批。");

            var issue = new ModelIssue
            {
                gameObject = null,
                modelName = "场景整体",
                type = IssueType.MaterialUsage,
                description =
                    $"材质使用效率低：平均每材质负责 {averageTrianglesPerMaterial:F1} 面，低于推荐值 {minTrianglesPerDC:F1} 面。当前有 {materialCount} 个材质，{totalMaterialAssignments} 次材质指派。",
                suggestion = "建议合并相似材质、使用纹理图集。"
            };
            issues.Add(issue);
        }
        else if (averageTrianglesPerMaterial > maxTrianglesPerDC * 2)
        {
            Debug.Log("平均每材质负责面数过高，可能存在过度复用材质但缺乏细节，但总体效率较优。");

            var issue = new ModelIssue
            {
                gameObject = null,
                modelName = "场景整体",
                type = IssueType.MaterialUsage,
                description =
                    $"材质复用度过高：平均每材质负责 {averageTrianglesPerMaterial:F1} 面，超过推荐最大值 {maxTrianglesPerDC * 2:F1} 面。可能缺乏视觉细节。",
                suggestion = "可以考虑适当增加材质贴图变化来提升视觉效果，或检查是否有不必要的高面数模型。"
            };
            issues.Add(issue);
        }
        else
        {
            Debug.Log("材质球使用与三角面分布比较合理。");
        }

        // 检查材质使用频率分布
        var lowUsageMaterials = materialUsageStats.Where(kvp => kvp.Value == 1).ToList();
        if (lowUsageMaterials.Count > materialCount * 0.3f) // 如果超过30%的材质只使用一次
        {
            var issue = new ModelIssue
            {
                gameObject = null,
                modelName = "场景整体",
                type = IssueType.MaterialUsage,
                description =
                    $"发现 {lowUsageMaterials.Count} 个材质仅使用一次，占总材质数的 {(lowUsageMaterials.Count / (float)materialCount * 100):F1}%。这可能导致不必要的DrawCall。",
                suggestion = "检查这些只使用一次的材质是否可以与其他材质合并，或者使用纹理图集来减少材质数量。"
            };
            issues.Add(issue);
        }
    }

    public static void AddIssue(GameObject obj, IssueType type, string description, string suggestion, List<ModelIssue> issues)
    {
        issues.Add(new ModelIssue
        {
            gameObject = obj,
            modelName = obj.name,
            type = type,
            description = description,
            suggestion = suggestion
        });
    }

    public static bool IsPowerOfTwo(int value)
    {
        return (value & (value - 1)) == 0;
    }
}
