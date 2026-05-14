-- AI Tavern mod entry. Phase 1: empty stubs.
-- RuntimeEnvSetup.Setup() calls LuaManager.LuaMod_Init() which invokes the
-- LuaMod_Init global registered here. PreloadedLua in ModSetting.asset must
-- include "modentry" so this file is loaded and these globals are bound.

function LuaMod_Init()
end


function LuaMod_DeInit()
end
