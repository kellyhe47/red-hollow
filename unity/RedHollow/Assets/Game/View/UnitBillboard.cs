using UnityEngine;
namespace RedHollow.Game.View
{
    /// <summary>
    /// World-facing 3D unit: a Lit capsule/hat volume with the canon albedo on
    /// standing cards. Yaw follows the owner (aim / walk) — never the camera —
    /// so a street-scale look sees the SIDE of the figure, not a postcard.
    /// Presentation only.
    /// </summary>
    public static class UnitBillboard
    {
        public const float HeroWidth = 1.95f;
        public const float HeroHeight = 4.50f;
        public const float HeroDepth = 1.05f;
        public const float MonsterWidth = 1.55f;
        public const float MonsterHeight = 3.80f;
        public const float MonsterDepth = 0.90f;

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
        /// Canon-painted 3D figure. <paramref name="albedo"/> is the punched standing
        /// sheet; extras (hat) keep the silhouette from reading as a plane.
        /// </summary>
        public static GameObject CreateFromCanon(
            string name, Texture2D albedo, string artKey, float height)
        {
            var isHero = artKey == "gunslinger" || artKey == "rancher" || artKey == "sawbones";
            var cowboyHat = artKey == "gunslinger" || artKey == "rancher";
            var behemoth = artKey == "bull_behemoth" || artKey == "BullBehemoth";
            var width = isHero ? HeroWidth : (behemoth ? 2.8f : MonsterWidth);
            var depth = isHero ? HeroDepth : (behemoth ? 1.80f : MonsterDepth);
            var tint = BodyTint(artKey);
            return CreateFigure(name, height, width, depth, albedo, tint, cowboyHat);
        }

        /// <summary>
        /// Legacy wrapper: keep the sprite as the front card on a volume, but do
        /// NOT yaw it at the camera.
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

            var root = new GameObject(name);

            var bodyMat = ViewLook.Lit(bodyColor, smoothness: 0.10f);
            var cardMat = albedo != null
                ? ViewLook.LitCutout(new Color(1.05f, 0.90f, 0.72f), albedo)
                : ViewLook.Lit(bodyColor * 1.15f, smoothness: 0.10f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "body";
            body.transform.SetParent(root.transform, false);
            var bodyH = height * 0.78f;
            body.transform.localScale = new Vector3(width * 0.70f, bodyH * 0.5f, depth);
            body.transform.localPosition = new Vector3(0f, bodyH * 0.5f, 0f);
            ViewLook.StripCollider(body);
            ViewLook.Paint(body, bodyMat, castShadows: true);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "head";
            head.transform.SetParent(root.transform, false);
            var headD = Mathf.Clamp(width * 0.38f, 0.28f, 0.55f);
            head.transform.localScale = Vector3.one * headD;
            head.transform.localPosition = new Vector3(0f, height - headD * 0.55f, depth * 0.08f);
            ViewLook.StripCollider(head);
            ViewLook.Paint(head, bodyMat, castShadows: true);

            if (cowboyHat)
            {
                var coat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                coat.name = "duster";
                coat.transform.SetParent(root.transform, false);
                coat.transform.localScale = new Vector3(width * 0.92f, height * 0.52f, depth * 1.12f);
                coat.transform.localPosition = new Vector3(0f, height * 0.36f, depth * 0.04f);
                ViewLook.StripCollider(coat);
                ViewLook.Paint(coat, ViewLook.Lit(new Color(0.22f, 0.13f, 0.07f), smoothness: 0.08f), castShadows: true);

                var brim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                brim.name = "hat_brim";
                brim.transform.SetParent(root.transform, false);
                brim.transform.localScale = new Vector3(headD * 1.85f, 0.035f, headD * 1.85f);
                brim.transform.localPosition = new Vector3(0f, height - 0.04f, depth * 0.04f);
                ViewLook.StripCollider(brim);
                ViewLook.Paint(brim, ViewLook.Lit(new Color(0.12f, 0.07f, 0.04f), smoothness: 0.08f), castShadows: true);

                var crown = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crown.name = "hat_crown";
                crown.transform.SetParent(root.transform, false);
                crown.transform.localScale = new Vector3(headD * 0.85f, 0.22f, headD * 0.95f);
                crown.transform.localPosition = new Vector3(0f, height + 0.08f, depth * 0.04f);
                ViewLook.StripCollider(crown);
                ViewLook.Paint(crown, ViewLook.Lit(new Color(0.14f, 0.08f, 0.05f), smoothness: 0.08f), castShadows: true);
            }

            // Front card faces local +Z (the unit's facing). Camera is south looking
            // +Z, so a unit aiming into the cavern shows its back/side — a real figure.
            PlaceCard(root.transform, "billboard", cardMat,
                new Vector3(0f, height * 0.5f, depth * 0.52f),
                Quaternion.identity,
                new Vector3(width, height, 1f));
            PlaceCard(root.transform, "card_back", cardMat,
                new Vector3(0f, height * 0.5f, -depth * 0.52f),
                Quaternion.Euler(0f, 180f, 0f),
                new Vector3(width, height, 1f));
            PlaceCard(root.transform, "card_right", cardMat,
                new Vector3(width * 0.42f, height * 0.5f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                new Vector3(depth, height * 0.96f, 1f));
            PlaceCard(root.transform, "card_left", cardMat,
                new Vector3(-width * 0.42f, height * 0.5f, 0f),
                Quaternion.Euler(0f, -90f, 0f),
                new Vector3(depth, height * 0.96f, 1f));

            AttachBlobShadow(root.transform, Mathf.Max(0.7f, width * 0.85f));
            return root;
        }

        private static void PlaceCard(
            Transform parent, string name, Material material,
            Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = name;
            card.transform.SetParent(parent, false);
            card.transform.localPosition = localPos;
            card.transform.localRotation = localRot;
            card.transform.localScale = localScale;
            ViewLook.StripCollider(card);
            if (material != null)
            {
                ViewLook.Paint(card, material, castShadows: true);
            }

            PromoteReadOrder(card.GetComponent<Renderer>());
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
