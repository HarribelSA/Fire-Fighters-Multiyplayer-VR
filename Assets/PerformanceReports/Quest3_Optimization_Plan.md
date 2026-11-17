# خطة التحسين — Meta Quest 3

> **لم يُنفَّذ أي بند من هذه الخطة.** كل بند يحتاج موافقة قبل التنفيذ، وقياسًا قبله وبعده على الجهاز.
> الأولويات: **P0** = يمنع العرض أو يسبب Crash · **P1** = أثر كبير على FPS/RAM · **P2** = تحسين متوسط · **P3** = تنظيف/صقل.

---

## P0 — قبل أي عرض على Quest Standalone

### P0-1: اختبار الجلسة المختلطة (السائق على PC + المساعد على Quest)
- **التصميم المعتمد:** السائق على Windows PCVR + G29، والمساعد على Quest 3 (APK). قفل Driver في `StationLocalRig.cs:142` **مقصود ولا يُغيَّر**.
- **المطلوب:** بناء Windows وبناء Android من نفس الـCommit، ونفس Network Prefabs، ونفس إعدادات Multiplayer Services/Vivox.
- **مقترح (اختياري، بعد موافقتك):** في الـLobby على Quest، منع اختيار مقعد السائق أو إظهار أنه للكمبيوتر فقط، حتى لا يعلق اللاعب على "Not Ready".
- **التحقق:** PC + Quest في نفس الجلسة: الـLobby، المحطة، القيادة، الحريق، والصوت.
- **الأثر:** الشبكة مباشرة. يُفضَّل أن يكون الكمبيوتر هو صاحب الجلسة (Authority) ليحمل منطق الحريق بدل Quest. البوت: في Solo على Quest يأخذ البوت دور السائق.

### P0-2: Override لأندرويد على Textures المحطة والـLobby (حد 2048، ASTC)
- **الملفات:** `Assets/FireStation/Textures/*` (32–36 ملفًا بحجم 4096، أبرزها `Concrete01_*` و`RollupDoor_*` و`InteriorDetails03_*` و`RoofDetails_*`)، و`Assets/Perfectvertex/firetrucks/Art/Textures/wheel_low_wheels_*` (4096 لعجلات)
- **السبب:** لا يوجد Override لأندرويد على أي Texture في المحطة/الـLobby. تقدير ~10MB لكل Texture بحجم 4096.
- **الفائدة (تقدير):** تخفيض 250–400MB من ذاكرة GPU/RAM، وتحميل أسرع.
- **المخاطرة:** منخفضة. فقد تفاصيل بسيط عن قرب، ويمكن إبقاء 4096 للأرضية القريبة فقط إذا لزم. **العملية تتطلب Reimport (وقت طويل).**
- **التحقق:** Memory Profiler قبل وبعد، ومقارنة بصرية بالنظارة.
- **الأثر:** لا شيء على الشخصيات أو الشبكة أو البوت.

### P0-3: فحص تفريغ المشاهد عند الانتقال للمدينة
- **الملفات:** `StationSessionFlow*.cs`، `StationMissionView.cs`، `FireCenterLobbyHost.cs`
- **السبب:** المحطة والـLobby (Textures ضخمة) + المدينة (LightingData 305MB) قد تتواجد معًا، أي خطر OOM.
- **الفائدة:** تجنب Crash عند الانتقال.
- **المخاطرة:** متوسطة. التفريغ يجب ألا يحذف NetworkObjects أو XR Origin (موجودة في S1Test).
- **التحقق:** Memory Profiler Snapshot بعد وصول المدينة: لا Textures من `FireStation/` في الذاكرة.
- **الأثر:** الشبكة: يجب أن يتم التفريغ محليًا (المشاهد مستثناة من NGO sync). البوت: يجب نقله قبل التفريغ.

### P0-4: التحقق من الـManifest (INTERNET + RECORD_AUDIO)
- **الملف:** Android Manifest الناتج من البناء (`Temp/gradleOut/...`) أو `ProjectSettings` (forceInternetPermission)
- **السبب:** Multiplayer Services وVivox يحتاجان الإنترنت والمايك.
- **التحقق:** `aapt dump permissions` على الـAPK، واختبار الاتصال والصوت.
- **الأثر:** الشبكة مباشرة.

---

## P1 — أثر كبير على الأداء

### P1-1: خبز (Bake) أضواء FireCenterLobby أو تقليلها
- **الملف:** `Assets/FireCenter/Scenes/FireCenterLobby.unity` (37 Spot Light بوضع Realtime)
- **السبب:** نفس أضواء المحطة لكنها في المحطة Baked وفي الـLobby Realtime، مع 4 أضواء Per-Pixel لكل كائن.
- **الفائدة (تقدير):** انخفاض كبير في تكلفة Fragment على GPU داخل الـLobby.
- **المخاطرة:** منخفضة-متوسطة. يحتاج Bake (وقت + Lightmaps إضافية بالذاكرة)، أو بديلًا أخف: تحويلها لـBaked وإعادة استخدام Lightmap المحطة.
- **التحقق:** OVR Metrics: GPU Utilization في الـLobby قبل/بعد.
- **الأثر:** الشخصيات تُضاء بـLight Probes بدل الأضواء اللحظية (مقبول).

### P1-2: تخفيف إعدادات URP لـQuest
- **الملف:** `Assets/VRMPAssets/Settings/URP-Performant.asset`
- **التغييرات المقترحة:** Soft Shadows ← Off، Cascades 2 ← 1، Additional Light Shadows ← Off، Max Additional Lights 4 ← 2، واختبار MSAA 4x ← 2x.
- **السبب:** كلها مكلفة على Adreno في VR.
- **الفائدة (تقدير):** توفير ملحوظ في GPU لكل المشاهد.
- **المخاطرة:** منخفضة. ظلال أقل نعومة. **ملاحظة:** نفس الـAsset مستخدم في كل مستويات الجودة وعلى PC، لذا الأفضل إنشاء URP Asset خاص بأندرويد.
- **التحقق:** مقارنة بصرية + GPU time.
- **الأثر:** لا شيء على الشبكة أو البوت.

### P1-3: Foveated Rendering + Depth Submission
- **الملف:** OpenXR Settings (Android) — Meta Quest feature / Foveation
- **السبب:** غير مفعل. Fixed/Eye-tracked foveation على Quest 3 يوفر GPU بشكل كبير.
- **الفائدة (تقدير):** 10–25% من GPU fragment (يحتاج قياس).
- **المخاطرة:** منخفضة. حواف مشوشة بسيطة في الأطراف.
- **التحقق:** OVR Metrics + فحص بصري للنصوص الجانبية (التابلت، الأسهم).

### P1-4: تقليل Overdraw النار والدخان والماء
- **الملفات:** `Assets/CityNightPresentation/Fireground/Runtime/FireZone.cs`، `HoseNozzle.cs` (maxParticles 300/160/140…)
- **السبب:** جزيئات شفافة كبيرة تملأ الشاشة عند الاقتراب.
- **الاقتراح:** خفض maxParticles بنحو 30–50%، وتصغير حجم الجزيئات القريبة من الكاميرا، و`maxParticleSize`، وحد للعدد الكلي.
- **المخاطرة:** منخفضة. شكل أقل كثافة.
- **التحقق:** RenderDoc Overdraw + GPU time عند الوقوف أمام النار.
- **الأثر:** التزامن: الحالة الشبكية (Intensity) لا تتغير. البوت لا يتأثر.

### P1-5: Draw Calls المدينة
- **الملف:** `demo_city_night.unity`
- **الاقتراح:** Static Batching للمباني الثابتة، وLODGroups للمباني البعيدة، وتقليل Far clip، والتحقق من Occlusion Culling الموجود.
- **الفائدة:** CPU Render thread أقل.
- **المخاطرة:** متوسطة. Static batching يزيد الذاكرة، ويجب الموازنة.
- **التحقق:** Frame Debugger: Batches/SetPass قبل/بعد.

### P1-6: LightingData و Lightmaps المدينة
- **الملفات:** `demo_city_night/LightingData.asset` (305MB)، و26 EXR
- **الاقتراح:** تقليل كثافة Light Probes، وإعادة Bake بدقة أقل، وOverride لأندرويد على Lightmaps (1024 أو ترميز Low Quality).
- **الفائدة:** RAM ووقت تحميل.
- **المخاطرة:** متوسطة. Bake طويل وتغيّر بصري.

---

## P2 — تحسين متوسط

| # | البند | الملف | السبب | المخاطرة | الأثر على الشخصيات/الشبكة/البوت |
|---|---|---|---|---|---|
| P2-1 | تفعيل Incremental GC | ProjectSettings | تقليل Spikes | منخفضة | لا يوجد |
| P2-2 | Animator Culling = CullUpdateTransforms للبوت والشخصيات البعيدة | Firefighter prefab / StationBotFollower | CPU | منخفضة. يجب التأكد أن IK لا يتعطل للشخصية المرئية | الشخصيات: تحقق من الجلوس وIK |
| P2-3 | دمج 11 SMR لكل شخصية (Mesh Baker أو دمج في DCC) | firefighter-tip-b 1 | Draw calls وSkinning | **عالية**: يمس العظام والمواد، يحتاج نسخة | الشخصيات مباشرة |
| P2-4 | LOD للشخصيات البعيدة (اللاعب الآخر على بُعد >8م) | Firefighter prefab | مثلثات | متوسطة | الشبكة لا يوجد |
| P2-5 | إزالة `FindFirstObjectByType` من LateUpdate | `CrewGuide.cs:65,87` | CPU صغير | منخفضة جدًا | البوت: Cache المرجع |
| P2-6 | Mesh Compression لـPipeRack.fbx وأمثاله | FireStation/Meshes | حجم/RAM | منخفضة | لا يوجد |
| P2-7 | URP Asset منفصل لأندرويد | VRMPAssets/Settings | فصل PC عن Quest | منخفضة | لا يوجد |
| P2-8 | Occlusion Culling للمحطة والـLobby | Sample / FireCenterLobby | Culling | منخفضة (Bake) | لا يوجد |
| P2-9 | مراجعة Audio Load Type (Streaming للطويلة، Mono) | S1Test audio | RAM | منخفضة | Vivox لا يتأثر |

## P3 — صقل

| # | البند | الملاحظة |
|---|---|---|
| P3-1 | Managed Stripping ← Low | اختبار Netcode/Reflection بعده |
| P3-2 | Application ID حقيقي | قبل التوزيع |
| P3-3 | ضبط CPU/GPU Performance Level عبر OpenXR Meta | بعد القياس |
| P3-4 | Dynamic Resolution | فقط إن بقي GPU على الحد |
| P3-5 | ضغط ملفات PNG المصدرية الضخمة للشخصية (61MB) | وقت الاستيراد فقط |

---

## ترتيب التنفيذ المقترح
1. P0-4 (Manifest) ← **Build أول (Windows + APK) + اختبار P0-1 (جلسة مختلطة) + قياس Baseline على Quest**
2. P0-2 (Textures) + P0-3 (Unload) ← قياس Memory
3. P1-2 + P1-3 (URP + Foveation) ← قياس GPU
4. P1-1 (أضواء Lobby) + P1-4 (Particles) ← قياس
5. P1-5/P1-6 (المدينة) ← قياس
6. P2 حسب نتائج الـProfiler
