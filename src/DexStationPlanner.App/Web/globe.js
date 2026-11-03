// DEX Station Planner globe. Runs inside WebView2; talks to the WPF host with JSON messages.
//
// Host -> page:  init { ionToken, theme }, scene { scene }, flyTo { lat, lon, range },
//                placeMode { enabled, name }, theme { theme }
// Page -> host:  ready, placed { lat, lon, height }, placeCancelled, log { level, message }
"use strict";

(function () {
  const host = window.chrome && window.chrome.webview;
  const send = (msg) => host && host.postMessage(msg);
  const log = (level, message) => send({ type: "log", level, message: String(message) });
  window.addEventListener("error", (e) => log("error", e.message));
  window.addEventListener("unhandledrejection", (e) => log("error", e.reason && e.reason.message ? e.reason.message : e.reason));

  const $ = (id) => document.getElementById(id);

  let viewer = null;
  let ionToken = "";
  let placing = false;
  let currentScene = null;
  let stationSphere = null;
  const stationEntities = [];

  // ---------------------------------------------------------------- imagery & terrain

  // Natural Earth II ships inside Cesium: it works offline and, being geographic, covers the poles,
  // which Web Mercator imagery (Esri, OSM, Bing) cannot. It always sits underneath.
  async function baseLayer() {
    const provider = await Cesium.TileMapServiceImageryProvider.fromUrl(
      Cesium.buildModuleUrl("Assets/Textures/NaturalEarthII"));
    return new Cesium.ImageryLayer(provider);
  }

  function onlineLayer(mode) {
    if (mode === "satellite") {
      if (ionToken) return Cesium.ImageryLayer.fromProviderAsync(Cesium.IonImageryProvider.fromAssetId(2));
      const esri = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/";
      return new Cesium.ImageryLayer(new Cesium.UrlTemplateImageryProvider({
        url: esri + "{z}/{y}/{x}",
        maximumLevel: 19,
        credit: new Cesium.Credit("Imagery © Esri, Maxar, Earthstar Geographics, and the GIS User Community"),
        // Where Esri has no detail (ice sheets, open ocean) it serves a grey "Map data not yet available"
        // tile with HTTP 200. Discard tiles matching that image so Cesium falls back to coarser imagery.
        // The reference tile is open Pacific Ocean at zoom 19.
        tileDiscardPolicy: new Cesium.DiscardMissingTileImagePolicy({
          missingImageUrl: esri + "19/262144/58254",
          pixelsToCheck: [
            new Cesium.Cartesian2(0, 0), new Cesium.Cartesian2(200, 20), new Cesium.Cartesian2(20, 200),
            new Cesium.Cartesian2(80, 110), new Cesium.Cartesian2(160, 130),
          ],
          disableCheckIfAllPixelsAreTransparent: true,
        }),
      }));
    }
    if (mode === "streets") {
      return new Cesium.ImageryLayer(new Cesium.OpenStreetMapImageryProvider({ url: "https://tile.openstreetmap.org/" }));
    }
    return null;
  }

  async function setImagery(mode) {
    const layers = viewer.imageryLayers;
    while (layers.length > 1) layers.remove(layers.get(layers.length - 1), true);
    const layer = onlineLayer(mode);
    if (layer) layers.add(layer);
  }

  async function setTerrain() {
    const badge = $("terrain-badge");
    if (ionToken) {
      const terrain = new Cesium.Terrain(Cesium.CesiumTerrainProvider.fromIonAssetId(1));
      terrain.errorEvent.addEventListener((e) => {
        badge.textContent = "Terrain: flat (ion terrain failed, check your token)";
        log("warn", "Cesium World Terrain unavailable: " + (e && e.message ? e.message : e));
      });
      viewer.scene.setTerrain(terrain);
      badge.textContent = "Terrain: Cesium World Terrain";
      return;
    }
    viewer.terrainProvider = new Cesium.EllipsoidTerrainProvider();
    badge.textContent = "Terrain: flat (add a Cesium ion token in Settings for 3D terrain)";
  }

  // ---------------------------------------------------------------- station rendering

  function enuFrame(scene) {
    return Cesium.Transforms.eastNorthUpToFixedFrame(Cesium.Cartesian3.fromDegrees(scene.longitude, scene.latitude, 0));
  }

  function toWorld(frame, p) {
    return Cesium.Matrix4.multiplyByPoint(frame, new Cesium.Cartesian3(p[0], p[1], 0), new Cesium.Cartesian3());
  }

  function clearStation() {
    for (const e of stationEntities) viewer.entities.remove(e);
    stationEntities.length = 0;
    stationSphere = null;
  }

  function add(entity) {
    stationEntities.push(viewer.entities.add(entity));
  }

  function renderScene(scene) {
    currentScene = scene;
    clearStation();
    if (!scene || !scene.isPlaced) return;

    const frame = enuFrame(scene);
    stationSphere = siteSphere(frame, scene);
    // Module labels only once the camera is close enough for them to separate.
    const nearOnly = new Cesium.DistanceDisplayCondition(0, Math.max(120, stationSphere.radius * 3));
    const farOnly = new Cesium.DistanceDisplayCondition(4000, Number.MAX_VALUE);

    add({
      name: scene.name,
      position: Cesium.Cartesian3.fromDegrees(scene.longitude, scene.latitude, 0),
      point: {
        pixelSize: 11, color: Cesium.Color.fromCssColorString("#e51414"),
        outlineColor: Cesium.Color.WHITE, outlineWidth: 2,
        heightReference: Cesium.HeightReference.CLAMP_TO_GROUND,
        disableDepthTestDistance: Number.POSITIVE_INFINITY,
        distanceDisplayCondition: farOnly,
      },
      label: {
        text: scene.name, font: "600 14px Segoe UI", fillColor: Cesium.Color.WHITE,
        outlineColor: Cesium.Color.BLACK, outlineWidth: 3, style: Cesium.LabelStyle.FILL_AND_OUTLINE,
        pixelOffset: new Cesium.Cartesian2(0, -18),
        heightReference: Cesium.HeightReference.CLAMP_TO_GROUND,
        disableDepthTestDistance: Number.POSITIVE_INFINITY,
        distanceDisplayCondition: farOnly,
      },
    });

    for (const v of scene.volumes) {
      const positions = v.ring.map((p) => toWorld(frame, p));
      const color = Cesium.Color.fromCssColorString(v.color);
      add({
        id: v.id,
        name: v.label,
        polygon: {
          hierarchy: new Cesium.PolygonHierarchy(positions),
          height: v.bottom,
          extrudedHeight: v.top,
          heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
          extrudedHeightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
          // Outlines aren't supported on ground-referenced polygons, so color alone separates shapes.
          material: color.withAlpha(v.kind === "corridor" ? 0.75 : 0.92),
        },
      });
      if (v.kind === "module") {
        const center = Cesium.BoundingSphere.fromPoints(positions).center;
        const carto = Cesium.Cartographic.fromCartesian(center);
        add({
          position: Cesium.Cartesian3.fromRadians(carto.longitude, carto.latitude, v.top + 1.5),
          label: {
            text: v.label, font: "12px Segoe UI", fillColor: Cesium.Color.WHITE,
            outlineColor: Cesium.Color.BLACK, outlineWidth: 3, style: Cesium.LabelStyle.FILL_AND_OUTLINE,
            heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
            disableDepthTestDistance: Number.POSITIVE_INFINITY,
            distanceDisplayCondition: nearOnly,
          },
        });
      }
    }

    for (const line of scene.lines) {
      const color = Cesium.Color.fromCssColorString(line.color);
      add({
        id: line.id,
        name: line.label,
        polyline: {
          positions: line.points.map((p) => toWorld(frame, p)),
          width: 3,
          clampToGround: true,
          material: line.kind === "Power" ? color : new Cesium.PolylineDashMaterialProperty({ color, dashLength: 12 }),
        },
      });
    }
  }

  /** Bounding sphere of everything drawn for the station (or a 50 m sphere at the origin). */
  function siteSphere(frame, scene) {
    const pts = [];
    for (const v of scene.volumes) for (const p of v.ring) pts.push(toWorld(frame, p));
    for (const l of scene.lines) for (const p of l.points) pts.push(toWorld(frame, p));
    if (pts.length === 0) return new Cesium.BoundingSphere(Cesium.Cartesian3.fromDegrees(scene.longitude, scene.latitude, 0), 50);
    return Cesium.BoundingSphere.fromPoints(pts);
  }

  function flyTo(lat, lon, range, toStation) {
    const sphere = toStation && stationSphere
      ? stationSphere
      : new Cesium.BoundingSphere(Cesium.Cartesian3.fromDegrees(lon, lat, 0), 1);
    viewer.camera.flyToBoundingSphere(sphere, {
      offset: new Cesium.HeadingPitchRange(0, Cesium.Math.toRadians(-40), toStation ? Math.max(80, sphere.radius * 2.6) : range || 3000),
      duration: 2.0,
    });
  }

  // ---------------------------------------------------------------- placement

  function setPlacing(enabled, name) {
    placing = enabled;
    $("place-name").textContent = name || "the station";
    $("place-banner").classList.toggle("hidden", !enabled);
    document.body.classList.toggle("placing", enabled);
  }

  function pickGlobe(windowPosition) {
    const ray = viewer.camera.getPickRay(windowPosition);
    if (!ray) return null;
    const cartesian = viewer.scene.globe.pick(ray, viewer.scene);
    return cartesian ? Cesium.Cartographic.fromCartesian(cartesian) : null;
  }

  // ---------------------------------------------------------------- startup

  async function init(options) {
    ionToken = options.ionToken || "";
    if (ionToken) Cesium.Ion.defaultAccessToken = ionToken;
    setTheme(options.theme);

    viewer = new Cesium.Viewer("globe", {
      baseLayer: await baseLayer(),
      baseLayerPicker: false,
      geocoder: false,
      animation: false,
      timeline: false,
      navigationHelpButton: false,
      fullscreenButton: false,
      sceneModePicker: true,
      homeButton: true,
      infoBox: false,
      selectionIndicator: true,
      terrainProvider: new Cesium.EllipsoidTerrainProvider(),
    });
    viewer.scene.globe.depthTestAgainstTerrain = true;
    viewer.scene.globe.enableLighting = false;

    await setTerrain();
    await setImagery($("imagery").value);
    $("imagery").addEventListener("change", (e) => setImagery(e.target.value));

    const handler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);
    handler.setInputAction((click) => {
      if (!placing) return;
      const carto = pickGlobe(click.position);
      if (!carto) return;
      setPlacing(false);
      send({
        type: "placed",
        lat: Cesium.Math.toDegrees(carto.latitude),
        lon: Cesium.Math.toDegrees(carto.longitude),
        height: ionToken ? carto.height : null,
      });
    }, Cesium.ScreenSpaceEventType.LEFT_CLICK);

    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && placing) {
        setPlacing(false);
        send({ type: "placeCancelled" });
      }
    });

    if (currentScene) renderScene(currentScene);
  }

  function setTheme(theme) {
    document.body.classList.toggle("light", theme === "light");
    document.body.classList.toggle("dark", theme !== "light");
  }

  function showError(message) {
    const el = $("error");
    el.textContent = message;
    el.classList.remove("hidden");
  }

  if (!window.Cesium) {
    showError("The CesiumJS library failed to load. Rebuild the app so wwwroot/cesium is copied next to the executable.");
    log("error", "Cesium.js missing");
    return;
  }

  function handle(msg) {
    switch (msg.type) {
      case "scene": renderScene(msg.scene); break;
      case "flyTo": flyTo(msg.lat, msg.lon, msg.range, msg.toStation); break;
      case "placeMode": setPlacing(msg.enabled, msg.name); break;
      case "theme": setTheme(msg.theme); break;
    }
  }

  function fail(e) {
    log("error", e && e.stack ? e.stack : e);
    showError(e && e.message ? e.message : String(e));
  }

  if (host) {
    // Messages that arrive while the viewer is still starting up are replayed once it is ready.
    let pending = [];
    host.addEventListener("message", async (event) => {
      const msg = event.data;
      try {
        if (msg.type === "init") {
          await init(msg);
          const queued = pending;
          pending = null;
          for (const m of queued) handle(m);
        } else if (pending) {
          pending.push(msg);
        } else {
          handle(msg);
        }
      } catch (e) {
        fail(e);
      }
    });
    send({ type: "ready" });
  } else {
    // Opened in a normal browser for development: show the globe with a demo point.
    init({ ionToken: "", theme: "dark" }).then(() =>
      renderScene({ name: "Demo", isPlaced: true, latitude: 72.5796, longitude: -38.4592, volumes: [], lines: [] }));
  }
})();
