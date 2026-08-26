using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// World-facing 3D unit figure — torso, head, hat, coat with thickness, arms.
    /// Yaw follows the owner (aim / walk), never the camera. Lit, not emissive,
    /// not Unlit: lanterns shade the planes. A painted card is not a figure.
    /// Presentation only.
    /// </summary>
    public static class UnitBillboard
    {
        // Street-scale person: ~1/7 of the 55° follow-cam frame, not a postage
        // stamp and not a 4.5u giant smear. Hat brim is wide so the 55° look
        // reads a disk, not a sliver.
        public const float HeroWidth = 1.90f;
        public const float HeroHeight = 4.05f;
        public const float HeroDepth = 1.35f;
        public const float MonsterWidth = 1.28f;
        public const float MonsterHeight = 2.55f;
        public const float MonsterDepth = 1.02f;

        /// <summary>Placeholder volume for a hero or monster class.</summary>
        public static GameObject CreatePlaceholder(VisualClass visualClass)
        {
            var isHero = visualClass == VisualClass.Hero;
            var width = isHero ? HeroWidth : MonsterWidth;
            var height = isHero ? HeroHeight : MonsterHeight;
            var depth = isHero ? HeroDepth : MonsterDepth;
            var tint = isHero
                ? new Color(0.48f, 0.28f, 0.12f)
                : new Color(0.32f, 0.40f, 0.16f);
            return CreateFigure(
                "placeholder_" + visualClass.ToString().ToLowerInvariant(),
                height, width, depth, null, tint, cowboyHat: isHero);
        }

        /// <summary>
        /// Constructed 3D figure. <paramref name="albedo"/> is accepted for call-site
        /// compatibility but is NOT wrapped onto the mesh — a painted standing sheet
        /// on a capsule is the smear Kelly rejected. Solid Lit materials catch lanterns.
        /// </summary>
        public static GameObject CreateFromCanon(
            string name, Texture2D albedo, string artKey, float height)
        {
            var isHero = artKey == "gunslinger" || artKey == "rancher" || artKey == "sawbones";
            var cowboyHat = artKey == "gunslinger" || artKey == "rancher";
            var behemoth = artKey == "bull_behemoth" || artKey == "BullBehemoth";
            var width = isHero ? HeroWidth : (behemoth ? 2.4f : MonsterWidth);
            var depth = isHero ? HeroDepth : (behemoth ? 1.70f : MonsterDepth);
            var tint = BodyTint(artKey);
            return CreateFigure(name, height, width, depth, albedo, tint, cowboyHat);
        }

        /// <summary>
        /// Legacy wrapper: keep the sprite as a child, but do NOT yaw it at the camera.
        /// </summary>
        public static GameObject WrapStandingSprite(GameObject spriteGo, float across)
        {
            var height = Mathf.Max(HeroHeight, across);
            var root = CreateFigure(
                spriteGo.name, height, HeroWidth, HeroDepth, null,
                new Color(0.42f, 0.24f, 0.12f), cowboyHat: false);
            spriteGo.name = "billboard";
            spriteGo.transform.SetParent(root.transform, false);
            spriteGo.transform.localRotation = Quaternion.identity;
            spriteGo.transform.localPosition = new Vector3(0f, height * 0.5f, HeroDepth * 0.52f);
            PromoteReadOrder(spriteGo.GetComponent<Renderer>());
            var facing = spriteGo.GetComponent<BillboardFacing>();
            if (facing != null)
            {
                Object.DestroyImmediate(facing);
            }

            return root;
        }

        public static GameObject CreateFigure(
            string name, float height, float width, float depth,
            Texture albedo, Color bodyColor, bool cowboyHat)
        {
            if (height < 0.5f)
            {
                height = HeroHeight;
            }

            _ = albedo;

            var root = new GameObject(name);

            var pick = root.AddComponent<CapsuleCollider>();
            pick.isTrigger = true;
            pick.radius = Mathf.Max(0.38f, width * 0.42f);
            pick.height = height;
            pick.center = new Vector3(0f, height * 0.5f, 0f);

            if (cowboyHat)
            {
                BuildGunslinger(root.transform, height, width, depth);
            }
            else
            {
                BuildCreature(root.transform, height, width, depth, bodyColor);
            }

            AttachBlobShadow(root.transform, Mathf.Max(0.55f, width * 0.78f));
            return root;
        }

        /// <summary>
        /// Readable gunslinger: two legs, torso, two arms, head, wide brim, crown,
        /// duster with thickness and a flared hem. Lit planes, not a cube blob.
        /// </summary>
        private static void BuildGunslinger(Transform root, float height, float width, float depth)
        {
            var coat = ViewLook.Lit(new Color(0.86f, 0.60f, 0.34f), smoothness: 0.14f, emit: false);
            var coatDark = ViewLook.Lit(new Color(0.64f, 0.42f, 0.22f), smoothness: 0.12f, emit: false);
            var hat = ViewLook.Lit(new Color(0.36f, 0.24f, 0.14f), smoothness: 0.10f, emit: false);
            var hatLit = ViewLook.Lit(new Color(0.72f, 0.50f, 0.28f), smoothness: 0.18f, emit: false);
            var skin = ViewLook.Lit(new Color(0.78f, 0.58f, 0.44f), smoothness: 0.18f, emit: false);
            var vest = ViewLook.Lit(new Color(0.60f, 0.42f, 0.26f), smoothness: 0.12f, emit: false);
            var pants = ViewLook.Lit(new Color(0.38f, 0.26f, 0.16f), smoothness: 0.10f, emit: false);
            var boots = ViewLook.Lit(new Color(0.22f, 0.14f, 0.09f), smoothness: 0.16f, emit: false);
            var gloves = ViewLook.Lit(new Color(0.28f, 0.18f, 0.12f), smoothness: 0.14f, emit: false);
            var kerchief = ViewLook.Lit(new Color(0.58f, 0.28f, 0.16f), smoothness: 0.12f, emit: false);
            var belt = ViewLook.Lit(new Color(0.36f, 0.22f, 0.12f), smoothness: 0.20f, emit: false);
            var wood = ViewLook.Lit(new Color(0.52f, 0.34f, 0.18f), smoothness: 0.22f, emit: false);
            var iron = ViewLook.Lit(new Color(0.42f, 0.38f, 0.34f), smoothness: 0.35f, emit: false);

            var hipY = height * 0.30f;
            var chestY = height * 0.52f;
            var shoulderY = height * 0.64f;
            var neckY = height * 0.72f;
            var headY = height * 0.80f;
            var brimY = height * 0.88f;
            var crownY = height * 0.97f;

            // Legs + boots — readable under the hem from 55°.
            Part(root, "leg_L", PrimitiveType.Cylinder,
                new Vector3(-0.16f, hipY * 0.52f, 0.02f),
                new Vector3(0.22f, hipY * 0.48f, 0.22f),
                Quaternion.identity, pants);
            Part(root, "leg_R", PrimitiveType.Cylinder,
                new Vector3(0.16f, hipY * 0.52f, 0.02f),
                new Vector3(0.22f, hipY * 0.48f, 0.22f),
                Quaternion.identity, pants);
            Part(root, "boot_L", PrimitiveType.Cube,
                new Vector3(-0.16f, 0.10f, 0.06f),
                new Vector3(0.24f, 0.18f, 0.38f),
                Quaternion.identity, boots);
            Part(root, "boot_R", PrimitiveType.Cube,
                new Vector3(0.16f, 0.10f, 0.06f),
                new Vector3(0.24f, 0.18f, 0.38f),
                Quaternion.identity, boots);

            // Torso / vest — the body inside the coat.
            Part(root, "torso", PrimitiveType.Capsule,
                new Vector3(0f, chestY, 0.04f),
                new Vector3(width * 0.52f, height * 0.18f, depth * 0.48f),
                Quaternion.identity, vest);

            // Duster: upper shell + flared hem + back panel so the coat has THICKNESS,
            // not one cube wrapping the whole person.
            Part(root, "duster", PrimitiveType.Cube,
                new Vector3(0f, shoulderY * 0.82f, 0.02f),
                new Vector3(width * 0.98f, height * 0.28f, depth * 0.78f),
                Quaternion.identity, coat);
            Part(root, "duster_hem", PrimitiveType.Cube,
                new Vector3(0f, height * 0.26f, -0.02f),
                new Vector3(width * 1.32f, height * 0.34f, depth * 1.05f),
                Quaternion.identity, coat);
            Part(root, "duster_back", PrimitiveType.Cube,
                new Vector3(0f, height * 0.42f, -depth * 0.48f),
                new Vector3(width * 1.08f, height * 0.62f, depth * 0.40f),
                Quaternion.identity, coatDark);
            Part(root, "duster_shoulder_L", PrimitiveType.Cube,
                new Vector3(-width * 0.42f, shoulderY, 0.02f),
                new Vector3(0.38f, 0.16f, depth * 0.62f),
                Quaternion.Euler(0f, 0f, 18f), coat);
            Part(root, "duster_shoulder_R", PrimitiveType.Cube,
                new Vector3(width * 0.42f, shoulderY, 0.02f),
                new Vector3(0.38f, 0.16f, depth * 0.62f),
                Quaternion.Euler(0f, 0f, -18f), coat);
            Part(root, "collar_L", PrimitiveType.Cube,
                new Vector3(-0.14f, neckY, 0.10f),
                new Vector3(0.16f, 0.22f, 0.18f),
                Quaternion.Euler(18f, -22f, -12f), coatDark);
            Part(root, "collar_R", PrimitiveType.Cube,
                new Vector3(0.14f, neckY, 0.10f),
                new Vector3(0.16f, 0.22f, 0.18f),
                Quaternion.Euler(18f, 22f, 12f), coatDark);

            Part(root, "belt", PrimitiveType.Cube,
                new Vector3(0f, hipY + 0.06f, 0.06f),
                new Vector3(width * 0.72f, 0.10f, depth * 0.58f),
                Quaternion.identity, belt);
            Part(root, "holster", PrimitiveType.Cube,
                new Vector3(0.28f, hipY - 0.04f, 0.12f),
                new Vector3(0.12f, 0.28f, 0.16f),
                Quaternion.Euler(12f, 0f, 8f), belt);

            // Arms — the silhouette of a person, not a coat-box.
            var armTilt = Quaternion.Euler(12f, 0f, 16f);
            Part(root, "arm_L", PrimitiveType.Cylinder,
                new Vector3(-width * 0.48f, chestY - 0.02f, 0.06f),
                new Vector3(0.18f, height * 0.16f, 0.18f),
                armTilt, coat);
            Part(root, "glove_L", PrimitiveType.Sphere,
                new Vector3(-width * 0.58f, chestY - height * 0.20f, 0.16f),
                Vector3.one * 0.16f,
                Quaternion.identity, gloves);
            var armTiltR = Quaternion.Euler(18f, 0f, -12f);
            Part(root, "arm_R", PrimitiveType.Cylinder,
                new Vector3(width * 0.46f, chestY - 0.04f, 0.10f),
                new Vector3(0.18f, height * 0.15f, 0.18f),
                armTiltR, coat);
            Part(root, "glove_R", PrimitiveType.Sphere,
                new Vector3(width * 0.52f, chestY - height * 0.18f, 0.22f),
                Vector3.one * 0.16f,
                Quaternion.identity, gloves);

            // Rifle in the right hand, along facing (+Z).
            Part(root, "rifle_stock", PrimitiveType.Cube,
                new Vector3(width * 0.42f, chestY - height * 0.12f, 0.38f),
                new Vector3(0.07f, 0.12f, 0.42f),
                Quaternion.Euler(18f, -8f, 0f), wood);
            Part(root, "rifle_barrel", PrimitiveType.Cylinder,
                new Vector3(width * 0.40f, chestY - height * 0.04f, 0.72f),
                new Vector3(0.06f, 0.38f, 0.06f),
                Quaternion.Euler(78f, 0f, 0f), iron);

            Part(root, "head", PrimitiveType.Sphere,
                new Vector3(0f, headY, 0.06f),
                Vector3.one * 0.36f,
                Quaternion.identity, skin);
            Part(root, "kerchief", PrimitiveType.Sphere,
                new Vector3(0f, neckY + 0.02f, 0.08f),
                new Vector3(0.28f, 0.16f, 0.28f),
                Quaternion.identity, kerchief);

            // Wide brim — THE read from 55°. Diameter ~1.5 so it is a disk, not a speck.
            var brimD = 1.38f;
            Part(root, "hat_brim", PrimitiveType.Cylinder,
                new Vector3(0f, brimY, 0.04f),
                new Vector3(brimD, 0.05f, brimD),
                Quaternion.identity, hatLit);
            Part(root, "hat_crown", PrimitiveType.Cube,
                new Vector3(0f, crownY, 0.04f),
                new Vector3(0.70f, 0.34f, 0.76f),
                Quaternion.identity, hat);
        }

        /// <summary>
        /// Cheap matching volume for shamblers and other non-cowboy units: hunched
        /// torso, head, two arms. Lit, not a card.
        /// </summary>
        private static void BuildCreature(
            Transform root, float height, float width, float depth, Color bodyColor)
        {
            var hide = ViewLook.Lit(bodyColor, smoothness: 0.08f, emit: false);
            var hideDark = ViewLook.Lit(
                new Color(bodyColor.r * 0.65f, bodyColor.g * 0.65f, bodyColor.b * 0.55f),
                smoothness: 0.07f, emit: false);
            var claw = ViewLook.Lit(
                new Color(bodyColor.r * 0.55f, bodyColor.g * 0.50f, bodyColor.b * 0.40f),
                smoothness: 0.12f, emit: false);

            var hunched = Quaternion.Euler(18f, 0f, 0f);
            Part(root, "body", PrimitiveType.Capsule,
                new Vector3(0f, height * 0.38f, 0.08f),
                new Vector3(width * 0.78f, height * 0.32f, depth * 0.85f),
                hunched, hide);
            Part(root, "head", PrimitiveType.Sphere,
                new Vector3(0f, height * 0.72f, depth * 0.22f),
                Vector3.one * Mathf.Clamp(width * 0.52f, 0.38f, 0.70f),
                Quaternion.identity, hideDark);
            Part(root, "arm_L", PrimitiveType.Cylinder,
                new Vector3(-width * 0.42f, height * 0.40f, 0.10f),
                new Vector3(0.16f, height * 0.18f, 0.16f),
                Quaternion.Euler(25f, 0f, 22f), hide);
            Part(root, "arm_R", PrimitiveType.Cylinder,
                new Vector3(width * 0.42f, height * 0.40f, 0.10f),
                new Vector3(0.16f, height * 0.18f, 0.16f),
                Quaternion.Euler(25f, 0f, -22f), hide);
            Part(root, "claw_L", PrimitiveType.Sphere,
                new Vector3(-width * 0.52f, height * 0.18f, 0.22f),
                Vector3.one * 0.18f,
                Quaternion.identity, claw);
            Part(root, "claw_R", PrimitiveType.Sphere,
                new Vector3(width * 0.52f, height * 0.18f, 0.22f),
                Vector3.one * 0.18f,
                Quaternion.identity, claw);
        }

        private static GameObject Part(
            Transform parent, string name, PrimitiveType type,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            ViewLook.StripCollider(go);
            ViewLook.Paint(go, material, castShadows: true);
            return go;
        }

        public static void PromoteReadOrder(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingOrder = 40;
            var sprite = renderer as SpriteRenderer;
            if (sprite != null)
            {
                sprite.sortingOrder = 40;
            }
        }

        public static void AttachBlobShadow(Transform owner, float radius)
        {
            if (owner == null)
            {
                return;
            }

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "blob_shadow";
            shadow.transform.SetParent(owner, false);
            shadow.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            shadow.transform.localScale = new Vector3(radius, 0.02f, radius * 0.78f);
            ViewLook.StripCollider(shadow);
            ViewLook.Paint(shadow, ViewLook.Unlit(new Color(0.04f, 0.02f, 0.01f, 0.85f)));
        }

        private static Color BodyTint(string artKey)
        {
            if (artKey == "gunslinger" || artKey == "rancher")
            {
                return new Color(0.42f, 0.24f, 0.12f);
            }

            if (artKey == "sawbones")
            {
                return new Color(0.50f, 0.48f, 0.40f);
            }

            if (artKey == "spitter")
            {
                return new Color(0.28f, 0.42f, 0.16f);
            }

            if (artKey == "ravager")
            {
                return new Color(0.36f, 0.22f, 0.12f);
            }

            if (artKey == "burrower")
            {
                return new Color(0.38f, 0.26f, 0.14f);
            }

            if (artKey == "bull_behemoth" || artKey == "BullBehemoth")
            {
                return new Color(0.34f, 0.20f, 0.10f);
            }

            return new Color(0.30f, 0.36f, 0.16f);
        }
    }
}
