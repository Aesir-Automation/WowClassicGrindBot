-- Independent known values for testing the renderer/capture path. No game-state
-- access, movement, targeting, casting, or saved keystroke history.
local palette = {
    0, 0x13579b, 0x2468ac, 1, 255, 256, 257, 65535, 65536,
    0x123456, 0xabcdef, 0xffffff, 0xff0000, 0x00ff00, 0x0000ff,
    0x808080, 0x010101, 0xfefefe, 0, 0x1e8481
}
local frame = CreateFrame("Frame", "PixelProofFrame", UIParent)
frame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 32, -64)
frame:SetSize(#palette * 16, 16)
frame:SetFrameStrata("TOOLTIP")
frame:SetAlpha(1)
local textures = {}
for index, value in ipairs(palette) do
    local texture = frame:CreateTexture(nil, "OVERLAY")
    texture:SetPoint("TOPLEFT", frame, "TOPLEFT", (index - 1) * 16, 0)
    texture:SetSize(16, 16)
    texture:SetColorTexture(math.floor(value / 65536) / 255,
        (math.floor(value / 256) % 256) / 255, (value % 256) / 255, 1)
    textures[index] = texture
end

local count = 0
local function setCounter(value)
    count = value % 16777216
    local texture = textures[#palette - 1]
    texture:SetColorTexture(math.floor(count / 65536) / 255,
        (math.floor(count / 256) % 256) / 255, (count % 256) / 255, 1)
end

-- Propagate the key so ordinary Space behavior still reaches the client.
-- This counts only Space and deliberately records no other key information.
if frame.SetPropagateKeyboardInput then
    frame:EnableKeyboard(true)
    frame:SetPropagateKeyboardInput(true)
    frame:SetScript("OnKeyDown", function(_, key)
        if key == "SPACE" then setCounter(count + 1) end
    end)
end

SLASH_PIXELPROOF1 = "/pixelproof"
SlashCmdList["PIXELPROOF"] = function(message)
    if message == "hide" then frame:Hide()
    elseif message == "show" then frame:Show()
    elseif message == "reset" then setCounter(0)
    else
        print("PixelProof: /pixelproof reset | hide | show. Space count: " .. count)
    end
end
