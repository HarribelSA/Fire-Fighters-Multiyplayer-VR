# تدقيق أداء Meta Quest 3 — مشروع Fire Fighters Multiplayer VR

> **نوع التدقيق:** قراءة فقط (Static Audit). لم يُعدَّل أي Script أو Asset أو إعداد، ولم يُشغَّل Play Mode أو Build أو Profiler.
> **التاريخ:** 2026-11-17 — **الفرع:** `rana` — **المحرر:** Unity 6000.3.25f1 / URP / OpenXR
>
> **مستويات الثقة المستخدمة:**
> - **مؤكد** = قيمة مقروءة مباشرة من إعدادات المشروع أو ملفات المشهد.
> - **تقدير** = حساب نظري من بيانات مؤكدة (مثل حجم Texture بعد الضغط).
> - **يحتاج قياس** = لا يمكن معرفته إلا على الجهاز بـ Profiler أو OVR Metrics Tool.
>
> **لا توجد في هذا التقرير أي أرقام FPS أو RAM أو GPU مقاسة.** كل رقم استهلاك هو تقدير نظري، ومذكور ذلك بجانبه.

---

## 1. ملخص تنفيذي

| البند | الحكم | الثقة |
|---|---|---|
| نموذج التشغيل المعتمد | **السائق على كمبيوتر Windows (PCVR + G29)، والمساعد على Quest 3 Standalone (APK)** | مؤكد من صاحب المشروع |
| جاهزية جهة Quest (المساعد) | **يحتاج تحسينات ذاكرة وGPU قبل العرض** | تقدير |
| أكبر عائق وظيفي | لا يوجد عائق في الأدوار؛ قفل Driver على Windows **مقصود**. المطلوب: اتصال جلسة مختلطة (PC + Android) | مؤكد / يحتاج اختبار |
| أكبر خطر أداء | ذاكرة Textures (عشرات الملفات 4096 بدون Override لأندرويد) + ملف LightingData بحجم 305MB للمدينة | مؤكد (القيم) / تقدير (الأثر) |
| أثقل مشهد GPU متوقع | `FireCenterLobby` (37 Spot Light لحظية) ثم `demo_city_night` (1326 MeshRenderer + نار وجزيئات) | تقدير |
| إعدادات URP | مناسبة جزئيًا. HDR مطفأ وSRP Batcher مفعل. لكن الظلال الناعمة و4 أضواء إضافية Per-Pixel مع ظلال وMSAA 4x تثقل GPU الجوال | مؤكد |
| مسار Stereo | OpenXR Android = **Single Pass Instanced** (جيد) | **مؤكد** |

---

## 2. مواصفات Meta Quest 3 المرجعية

| البند | القيمة | ملاحظة |
|---|---|---|
| المعالج | Snapdragon XR2 Gen 2 (Adreno 740) | معروف علنًا |
| الذاكرة الكلية | 8GB LPDDR5 | **ليست كلها متاحة للتطبيق**: النظام وخدمات Meta وGuardian والـCompositor تأخذ جزءًا كبيرًا. الحد الفعلي للتطبيق **يحتاج تحقق على الجهاز** عبر `adb shell dumpsys meminfo` أو OVR Metrics. |
| الدقة | 2064×2208 لكل عين | الدقة الافتراضية لـ eye buffer يحددها Runtime، وrenderScale=1 في المشروع |
| معدلات التحديث | 72 / 80 / 90 / 120 Hz | |
| ميزانية الإطار | 72Hz = 13.9ms، 90Hz = 11.1ms، 120Hz = 8.3ms | يجب أن يكون CPU **و** GPU كلٌ منهما أقل من الميزانية |

---

## 3. إعدادات Android / XR / البناء (مؤكد — مقروءة من ProjectSettings)

| الإعداد | القيمة الحالية | التقييم |
|---|---|---|
| Active Build Target | Android | ✅ |
| Graphics API | Vulkan فقط (Auto مطفأ) | ✅ مناسب لـ Quest |
| Scripting Backend | IL2CPP / ARM64 | ✅ |
| IL2CPP Configuration | Release | ✅ |
| Managed Stripping | Minimal | ⚠️ يمكن رفعه إلى Low/Medium بعد اختبار (حجم APK ووقت التحميل) |
| Min SDK | API 34 | ✅ متوافق مع Quest 3 |
| Color Space | Linear | ✅ |
| Texture Compression | ASTC | ✅ |
| Stereo (PlayerSettings legacy) | MultiPass | ℹ️ **غير مستخدم فعليًا**، لأن OpenXR يتجاوزه |
| **OpenXR Android Render Mode** | **SinglePassInstanced** | ✅ الأهم، وهو صحيح |
| OpenXR Depth Submission | None | ⚠️ يُفضَّل 16-bit لتحسين Reprojection/ASW (اختبار) |
| OpenXR Features (Android) | MetaQuest, HandTracking, MetaHandTrackingAim, HandInteractionProfile, OculusTouch, TouchPlus, TouchPro, CompositionLayers | ✅ |
| Foveated Rendering | **غير مفعل كميزة ظاهرة** (لا توجد Feature خاصة به في القائمة) | ⚠️ فرصة كبيرة لتوفير GPU |
| GPU Skinning | مفعل | ✅ |
| Multithreaded Rendering | مفعل | ✅ |
| Graphics Jobs | مطفأ | ℹ️ مقبول على Vulkan/Quest (يحتاج اختبار قبل التفعيل) |
| **Incremental GC** | **مطفأ** | ⚠️ قد يسبب Spikes عند جمع القمامة → تقطيع في VR |
| Development Build | مطفأ | ✅ |
| Build App Bundle | مطفأ (APK) | ✅ لـ Sideload |
| INTERNET Permission (force) | false | ⚠️ **يجب التحقق** أن Multiplayer Services/Vivox تضيف الإذن تلقائيًا في الـManifest النهائي |
| Microphone Permission | لم يُفحص في Manifest | ⚠️ Vivox يحتاج RECORD_AUDIO، والتحقق على الجهاز مطلوب |
| Application ID | `com.DefaultCompany.VRMultiplayer` | ⚠️ اسم افتراضي، يُغيَّر قبل التوزيع |
| XR Loaders Android | OpenXRLoader فقط، InitOnStart=true | ✅ (Simulation loader على Standalone فقط) |

---

## 4. إعدادات GPU / URP (مؤكد)

**Pipeline المستخدم على كل مستويات الجودة:** `Assets/VRMPAssets/Settings/URP-Performant.asset`
**مستوى الجودة الافتراضي لأندرويد:** Q0 Performant

| الإعداد | القيمة | تقييم لـ Quest 3 |
|---|---|---|
| Render Scale | 1.0 | ✅ (يمكن 0.9 إذا احتاج GPU) |
| MSAA | 4x | ⚠️ جودة ممتازة لكن مكلف مع Tile memory، فـ2x خيار للاختبار |
| HDR | مطفأ | ✅ ممتاز للجوال |
| Depth Texture / Opaque Texture | مطفأ / مطفأ | ✅ (يمنع نسخ إضافية) |
| Main Light Shadows | مفعل، 2048، مسافة 15م، **2 Cascades** | ⚠️ الـCascades على الجوال مكلفة، و1 Cascade كافية لمسافة 15م |
| **Soft Shadows** | **مفعل** | ⚠️ مكلف على Adreno |
| Additional Lights | Per-Pixel، حتى 4 لكل كائن | ⚠️ مكلف مع مشاهد بها عشرات الأضواء |
| **Additional Light Shadows** | **مفعل** | ⚠️ (لكن أغلب الأضواء في المشاهد ظلها None أو Baked) |
| SRP Batcher | مفعل | ✅ |
| Dynamic Batching | مطفأ | ✅ |
| LOD Cross-fade | مفعل | ℹ️ Dither إضافي في Fragment |
| Store Actions | Auto | ✅ |
| GPU Resident Drawer | Disabled | ✅ مناسب (غير مفيد عادة على Quest) |
| Rendering Path | Forward | ✅ (Forward+ غير مستخدم) |
| Depth Priming | Disabled | ✅ للجوال |
| Renderer Features | 0 | ✅ لا Post-processing أو Features إضافية |
| Post-Process Data | لا يوجد | ✅ |
| Quality Q0 | vSync 0, lodBias 0.4, skinWeights 2, particleRaycastBudget 4, realtime reflection probes مطفأ | ✅ إعدادات خفيفة |

**Shaders مستخدمة (مؤكد):** Lit, SimpleLit, Unlit, ParticlesUnlit, TMP_SDF / Mobile / Overlay, عدة Shader Graphs (Interactable, Fresnel_Transparent, Unity_Hand_Noise, SimpleLitDither), NightSky, RoadLightPool, NavigationArrow, StationViewFade, UIAlwaysOnTop.
- ⚠️ `Lit.shader` هو الأكثر استخدامًا وهو الأثقل. وجود Shader Graphs شفافة (Fresnel) فوق مساحات كبيرة يسبب Overdraw.
- ℹ️ `m_LightmapStripping: 0` و`m_FogStripping: 0` (Automatic) → جيد لعدد Variants.

---

## 5. ميزانية الإطار (FPS)

| الهدف | الميزانية | التوصية |
|---|---|---|
| 72 Hz | 13.9ms | **الهدف الموصى به للعرض الأول** (Demo) |
| 90 Hz | 11.1ms | بعد تنفيذ تحسينات P0+P1 فقط |
| 120 Hz | 8.3ms | غير واقعي لهذا المحتوى |

> **لا يمكن ذكر FPS متوقع رقميًا** دون قياس. كل ما يمكن قوله (تقدير): المحتوى الحالي (مدينة 1326 MeshRenderer + نار + 3 شخصيات + ظلال ناعمة) **يحتاج تحسينات** ليثبت على 72Hz.

---

## 6. تقدير الذاكرة (RAM) — **تقدير نظري، يحتاج قياس**

### 6.1 Textures (الأخطر)
| المشهد | عدد Textures | ملفات ≥4096 | بدون Override لأندرويد | حجم المصدر على القرص |
|---|---|---|---|---|
| S1Test | 101 | 4 | 86 | 356MB |
| FireCenterLobby | 142 | **32** | **142 (كلها)** | 565MB |
| Sample (المحطة) | 182 | **36** | **182 (كلها)** | 716MB |
| demo_city_night | 100 | 4 | 58 | 224MB |

**حساب تقديري (ASTC 6x6 + Mipmaps):** Texture 4096 ≈ **~10MB** في الذاكرة، وTexture 2048 ≈ **~2.5MB**.
- المحطة والـLobby يشتركان في نفس Textures (`Assets/FireStation/Textures`)، لذلك هي ذاكرة مشتركة عند تحميلهما معًا.
- **تقدير: 36 × 10MB ≈ 360MB لـTextures بحجم 4096 وحدها في مشهد المحطة**، بالإضافة إلى ~150 Texture أخرى.
- أكبرها: `Concrete01_*` (Albedo/Normal/Specular بحجم 4096)، و`RollupDoor_MetallicSmoothness`، و`InteriorDetails03_*`، و`RoofDetails_*`.
- Textures الشخصية (`firefighter-tip-b 1/textures`) بحجم 2048 ومضغوطة ASTC 6x6 (لها Override) ✅. لكن ملفات PNG المصدرية بحجم 33–61MB، فوقت الاستيراد كبير لكن الأثر على الذاكرة مقبول.
- `Perfectvertex/firetrucks`: `coverL_fabric_Normal` بحجم 2048، و`wheel_low_wheels_*` بحجم 4096 **لعجلات**. هذا هدر واضح.

### 6.2 الإضاءة المخبوزة
- `demo_city_night/LightingData.asset` = **305MB على القرص** (مؤكد). الجزء الأكبر غالبًا Light Probes وبيانات GI. **خطر عالٍ على RAM وعلى وقت التحميل** (تقدير؛ يحتاج قياس بـ Memory Profiler).
- 26 Lightmap بصيغة EXR بحجم 2048 (مؤكد)، بدون Override لأندرويد. تقدير: 26 × (4–8MB) ≈ **100–200MB** حسب ترميز Lightmap.
- المدينة تملك Occlusion Culling Data ✅. المحطة والـLobby و S1Test **لا تملك** Occlusion Data.

### 6.3 Meshes والصوت
- `FireStation/Meshes/PipeRack.fbx` = 18MB، Mesh Compression = Off.
- الصوت: S1Test 12 ملفًا (18MB مصدر)، والباقي صغير. Load Type يحتاج مراجعة (لم يُفحص لكل ملف).

### 6.4 المشاهد المحمّلة معًا (Additive)
S1Test (نظام) + FireCenterLobby (محلي) ← ثم Sample (المحطة) ← ثم demo_city_night.
- ⚠️ **يجب التحقق** من أن المحطة والـLobby تُفرَّغ فعلًا (`UnloadSceneAsync`) عند الانتقال للمدينة، ومن استدعاء `Resources.UnloadUnusedAssets` بعدها، وإلا ستتراكم الذاكرة: Textures المحطة (~400MB+ تقديرًا) + Lightmaps وLightingData المدينة.
- **الخلاصة (تقدير):** ذروة الذاكرة أثناء الانتقال للمدينة قد تقترب من حد التطبيق على Quest 3. هذا **أعلى خطر Crash (OOM)**.

### جدول RAM التقديري
| المكون | التقدير | الثقة |
|---|---|---|
| Textures المحطة/Lobby | 400–600MB | تقدير |
| Textures المدينة + الشاحنة | 150–250MB | تقدير |
| Lightmaps المدينة | 100–200MB | تقدير |
| LightingData المدينة | حتى ~300MB | تقدير (حجم القرص مؤكد) |
| 3 شخصيات (Meshes + Textures) | 60–100MB | تقدير |
| Netcode + Vivox + XR + Mono heap | 150–300MB | تقدير |
| **المجموع عند الذروة** | **1.2–1.8GB+** | **يحتاج قياس** |

---

## 7. إحصائيات المشاهد (مؤكد من ملفات .unity؛ الأرقام لا تشمل محتوى الـPrefabs)

| المشهد | حجم الملف | GameObjects | MeshRenderer مباشر | Prefab Instances | Lights | Reflection Probes | LODGroups | MeshColliders | Particle Systems | Occlusion Data |
|---|---|---|---|---|---|---|---|---|---|---|
| S1Test | 1.2MB | 360 | 79 | 14 | 1 (Directional، بدون ظل) | 0 | 0 | 1 | 1 | ❌ |
| FireCenterLobby | 3.0MB | 730 | 90 | 327 | **39** (37 Spot لحظية مفعلة + Directional بظل Soft) | 19 | 28 | 85 | 0 | ❌ |
| Sample (المحطة) | 2.1MB | 287 | 96 | 326 | 45 (34 Spot **Baked** + 3 Point Baked + 1 Directional Mixed بظل Hard + القليل Realtime) | 19 | 28 | 92 | 0 | ❌ |
| demo_city_night | **12MB** | 546 | **1326** | **2207** | 13 (5 Spot لحظية + 3 Point لحظية + Directional Baked) | 13 | 12 | 2 | 15 + النار الإجرائية | ✅ |

**ملاحظات:**
- **FireCenterLobby:** 37 Spot Light لحظية (Lightmapping=Realtime) بدون ظلال، ومع حد 4 أضواء لكل كائن Per-Pixel → **تكلفة Fragment عالية جدًا** على GPU (تقدير). هذه الأضواء في المحطة نفسها مخبوزة (Baked)، أما في الـLobby فهي Realtime. **أثقل نقطة GPU في المشروع (تقدير، ثقة متوسطة-عالية).**
- **demo_city_night:** 1326 MeshRenderer مباشر + 2207 Prefab instance → Draw Calls وCulling CPU مرتفعة. يخففها Occlusion Culling الموجود وSRP Batcher. **أثقل مشهد CPU (تقدير).** 139 كائنًا عليها Static flags.
- لا يوجد LOD لمعظم مباني المدينة (12 LODGroup فقط) → **يحتاج تحقق** من عدد المثلثات المرئية بـ Frame Debugger.
- 19 Reflection Probe في المحطة/الـLobby: مقبولة إن كانت Baked (Realtime probes مطفأة في Q0 ✅).

---

## 8. الشخصيات والعظام وIK (مؤكد من المشهد المفتوح S1Test)

**شخصية رجل الإطفاء الواحدة (FirefighterPlacement):**
| الجزء | المثلثات | العظام |
|---|---|---|
| Kurtka (الجاكيت) | 17,510 | 21 |
| breath_system2 | 7,038 | 10 |
| GasMask_LP1 | 6,361 | 3 |
| Shtani | 5,168 | 11 |
| Kaska (الخوذة) | 5,146 | 4 |
| FireGloves ×2 | 7,240 | 18 |
| Sapog ×2 | 4,936 | 4 |
| Poyas | 1,852 | 3 |
| **المجموع** | **~55,000 مثلث، 11 SkinnedMeshRenderer، ~13 مادة** | |

**يد XR (لكل لاعب محلي):** يدان × 8,620 مثلثًا (26 عظمة) + نسخة 2,702 مثلثًا + Pinch visuals.

**التقييم:**
- 3 شخصيات (لاعبان + بوت) = **~165,000 مثلث Skinned + 33 SkinnedMeshRenderer + ~39 Draw Call** للشخصيات وحدها (تقدير مبني على أرقام مؤكدة). مع الرسم مرتين (عينان): SPI يحل التكرار، لكن Skinning يحدث مرة واحدة ✅ (GPU Skinning مفعل).
- ⚠️ **11 SMR منفصلة لكل شخصية**، والدمج في 1–2 Mesh يوفر Draw Calls وSkinning dispatches.
- ⚠️ `Animator.cullingMode = AlwaysAnimate`: الشخصيات تُحسب حتى خارج الرؤية.
- **IK:** `RigBuilder` (Animation Rigging) مع TwoBoneIK لليدين والرجلين + Head IK. تكلفة Animation Rigging على CPU (Jobs) **معتدلة لـ3 شخصيات (تقدير)** وليست الخطر الأكبر. يحتاج قياس `PreLateUpdate.DirectorUpdateAnimationEnd`.
- `skinWeights=2` في Q0 ✅ يقلل تكلفة Skinning.
- **حكم: الشخصيات لن تكون السبب الأول لهبوط FPS، لكنها تأخذ جزءًا ملموسًا من ميزانية GPU (~165k مثلث) وDraw Calls.** الثقة: متوسطة.

---

## 9. الشبكة (Netcode for GameObjects 2.13.2 + Multiplayer Services + Vivox)

| البند | القيمة | الثقة |
|---|---|---|
| Tick Rate | 30 | مؤكد |
| Topology | Distributed Authority عبر Multiplayer Services (في الـInspector يظهر ClientServer وتُضبط عند إنشاء الجلسة) | مؤكد من الكود / يحتاج تحقق Runtime |
| NetworkObjects في S1Test | 10 | مؤكد |
| NetworkTransform | 0 (المزامنة عبر RPC مخصص) | مؤكد |
| حالة الأفاتار | RPC بمعدل 10Hz، و20Hz عند الجلوس في المركبة (Seat-space) | مؤكد من الكود |
| حالة Fireground | NetworkVariable واحد (FiregroundState) يكتبه المالك | مؤكد |
| اللاعبون | 2 كحد أقصى | مؤكد |

**التقييم:** حمل الشبكة على CPU **منخفض** (لاعبان، 10–20Hz، Struct صغير). Vivox يضيف Thread صوتي وترميز Opus، وهو تكلفة CPU ثابتة معتدلة (تقدير). الخطر الأكبر هنا **وظيفي**: صلاحيات الإنترنت والمايك في الـManifest، وزمن الاتصال على Wi-Fi.

---

## 10. البوت (StationSoloBotTest / StationBotFollower / FiregroundBot)

- يعمل فقط في وضع Solo، لذلك لا يوجد بوت مع لاعبَين حقيقيين ✅.
- يستخدم نفس شخصية رجل الإطفاء (~55k مثلث) + Animator + IK.
- منطق التتبع خفيف (Update بحساب مسافات، دون NavMesh ثقيل).
- ⚠️ `FindFirstObjectByType<StationSoloBotTest>()` **يُستدعى كل إطار** في `CrewGuide.LateUpdate` (HeadArrows/LobbyArrows) → بحث في كل الكائنات كل إطار. التكلفة صغيرة لكنها غير ضرورية (مؤكد من الكود، والأثر تقدير).
- **حكم: Multiplayer + البوت لا يضغطان CPU بشكل كبير** (ثقة متوسطة). الضغط الأكبر من عدد الكائنات في المدينة والفيزياء والجزيئات.

---

## 11. المؤثرات (النار، الدخان، الماء، الخرطوم)

| العنصر | التفاصيل (مؤكد من الكود) | الأثر |
|---|---|---|
| Particle Systems الإجرائية | maxParticles: 300، 160، 140، 90×2، 80، 60؛ rateOverTime حتى 150 | ⚠️ **Overdraw** شفاف كبير عند الاقتراب من النار، وهو أخطر شيء على GPU في الجوال |
| مناطق النار | 3 FireZone في المدينة، كل منها بـPoint Light (shadows=None) | ⚠️ 3 أضواء Realtime إضافية Per-Pixel على المباني المحيطة |
| تصادم الماء مع العالم | Particle collision (World) | ⚠️ تكلفة CPU (Physics queries) و`particleRaycastBudget=4` في Q0 يحدها ✅ |
| الخرطوم | Verlet rope (CPU) | منخفض-معتدل |
| الصوت الإجرائي | `AudioClip.Create` عند التهيئة | منخفض |
| الأدلة المرئية (CrewGuide/TaskBeacon) | Unlit شفاف + Halo spheres (10×16) | منخفض |
| أسهم الطريق | `Graphics.DrawMeshInstanced` | ✅ فعّال |
| المدينة | 15 Particle System (مطر/بخار…) | ⚠️ Overdraw إضافي |

---

## 12. تحميل المشاهد والانتقالات

- التحميل Additive مع شاشة تحميل (MissionLoadingScreen) ✅. لكن:
  - ⚠️ `LightingData.asset` بحجم 305MB + 12MB مشهد + 2207 Prefab ← **وقت تحميل المدينة على Quest قد يكون طويلًا (عشرات الثواني، تقدير)** مع خطر تجمد الإطار (Hitch) يسبب سقوط الـCompositor إلى شاشة الانتظار.
  - ⚠️ المحطة والـLobby يستخدمان نفس الإحداثيات، ويجب التأكد من عدم بقائهما في الذاكرة أثناء المدينة.
  - ℹ️ استخدام `FindObjectsByType` في عدة سكربتات (CrewGuide، SeatedDispatchLobby، StationSceneSetup، FiregroundOperation…). أغلبها كل 0.5 ثانية أو عند التهيئة، لكن بعضها كل إطار.

---

## 13. التحليل الحراري (Thermal) — **يحتاج قياس**

- Quest 3 يخفض الترددات (CPU/GPU Level) عند الحرارة. الجلسة 15–30 دقيقة مع GPU شبه مشبع (ظلال ناعمة + 37 ضوء في الـLobby + Overdraw النار) **مرشحة للتسخين** (تقدير).
- لا يستخدم المشروع حاليًا (حسب ما فُحص) واجهة لضبط `CPU/GPU Performance Level` أو Dynamic Resolution. لم يُعثر على Feature خاصة بذلك في OpenXR features المفعلة.
- **يجب** تسجيل OVR Metrics (CPU/GPU Level، Temperature، Battery) طوال اختبار 30 دقيقة.

---

## 14. مشاكل وظيفية تؤثر على الإصدار Standalone (مؤكد)

1. **دور السائق مقصور على Windows PCVR (مقصود حسب التصميم):**
   `StationLocalRig.cs:81,142`. السائق يعمل على الكمبيوتر مع G29، و**Quest يشغّل دور المساعد فقط**. النتيجة: لاعب Quest يجب أن يختار Assistant دائمًا، ويجب أن يُمنع (أو يُوضَّح له) اختيار مقعد السائق في الـLobby.
2. **جلسة مختلطة PC + Android:** يلزم بناءان (Windows + Android APK) من **نفس الكود ونفس قائمة Network Prefabs ونفس نسخة NGO**، وإلا تفشل المزامنة. يجب اختبار الاتصال عبر Multiplayer Services وVivox بين المنصتين.
3. **الأداء على Quest يخص المساعد:** Quest يحمل كل المشاهد (Lobby، المحطة، المدينة، الحريق) ويرسم السائق (شخصية كاملة)، والمساعد هو من يقف أمام النار ويحمل النوزل، **فهو يتحمل أثقل مشهد GPU (Overdraw النار والماء).**
4. **السائق على الكمبيوتر:** نفس الـURP Asset مستخدم على PC، والـPC قادر عليه. لكن إذا كان الكمبيوتر هو صاحب الجلسة (Session Owner/Authority) فهو يحسب منطق الحريق، وهذا أفضل لـQuest.
5. Application ID افتراضي.

---

## 15. جدول المخاطر

| # | الخطر | الشدة | الثقة | المصدر |
|---|---|---|---|---|
| 1 | جلسة مختلطة PC (Driver) + Quest (Assistant) غير مختبرة؛ أي اختلاف بين البناءين يكسر المزامنة | **High** | يحتاج اختبار | Build Windows + APK |
| 2 | ذاكرة Textures: 32–36 ملفًا بحجم 4096 بدون Override لأندرويد في المحطة/الـLobby | **Critical** | مؤكد (القيم) / تقدير (الأثر) | FireStation/Textures |
| 3 | LightingData للمدينة 305MB + 26 Lightmap EXR، فخطر OOM ووقت تحميل طويل | **High** | مؤكد (الحجم) / يحتاج قياس | demo_city_night/ |
| 4 | 37 Spot Light لحظية في FireCenterLobby | **High** | مؤكد (العدد) / تقدير (الأثر) | FireCenterLobby.unity |
| 5 | Overdraw جزيئات النار والدخان والماء عند الاقتراب | **High** | تقدير | Fireground Runtime |
| 6 | 1326 MeshRenderer + 2207 Prefab في المدينة، أي Draw Calls عالية وLOD قليل | **High** | مؤكد (العدد) / يحتاج قياس | demo_city_night.unity |
| 7 | Soft Shadows + 2 Cascades + ظلال أضواء إضافية | Medium | مؤكد | URP-Performant.asset |
| 8 | Incremental GC مطفأ، فـSpikes عند GC | Medium | مؤكد (الإعداد) | ProjectSettings |
| 9 | 11 SMR لكل شخصية × 3، وAnimator AlwaysAnimate | Medium | مؤكد | Firefighter prefab |
| 10 | عدم التحقق من إذن INTERNET/RECORD_AUDIO في Manifest | Medium | يحتاج تحقق | Vivox/MPS |
| 11 | عدم وجود Foveated Rendering / Depth Submission | Medium | مؤكد (غير مفعل) | OpenXR settings |
| 12 | MSAA 4x | Low-Medium | مؤكد | URP |
| 13 | `FindFirstObjectByType` كل إطار في CrewGuide | Low | مؤكد | CrewGuide.cs:65,87 |
| 14 | Managed Stripping = Minimal، أي APK أكبر | Low | مؤكد | ProjectSettings |
| 15 | Application ID افتراضي | Low | مؤكد | ProjectSettings |

---

## 16. أدوات القياس الموصى بها

| الأداة | الاستخدام |
|---|---|
| **OVR Metrics Tool** (على الجهاز) | FPS، CPU/GPU Level، GPU Utilization، Stale Frames، Temperature، Battery |
| **Unity Profiler** (Development Build + Autoconnect عبر USB/Wi-Fi) | CPU Main/Render threads، GC Alloc، Animation، Physics، Scripts |
| **Memory Profiler package** | Snapshot في كل مرحلة (Lobby، Station، City، Fireground) |
| **Frame Debugger** (Remote) | Draw Calls، SetPass، Batches |
| **RenderDoc / Snapdragon Profiler / Meta Quest Developer Hub (Perfetto)** | تكلفة GPU لكل Pass، Overdraw، Bandwidth |
| `adb shell dumpsys meminfo <package>` | ذاكرة التطبيق الفعلية (PSS) |
| `adb logcat -s VrApi Unity` | سطر VrApi كل ثانية: FPS، Stale، CPU/GPU level، Temp |

خطة الاختبار التفصيلية (15–30 دقيقة) موجودة في `Quest3_Build_Test_Checklist.md`، وخطة التحسين في `Quest3_Optimization_Plan.md`.
