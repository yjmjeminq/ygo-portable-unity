-- Speed World 2 (story riding duels).
local s,id=GetID()
local SPC=0x0091

function s.initial_effect(c)
	c:EnableCounterPermit(SPC)
	c:SetCounterLimit(SPC,12)
	local activation=Effect.CreateEffect(c)
	activation:SetType(EFFECT_TYPE_ACTIVATE)
	activation:SetCode(EVENT_FREE_CHAIN)
	c:RegisterEffect(activation)

	local gain=Effect.CreateEffect(c)
	gain:SetType(EFFECT_TYPE_FIELD+EFFECT_TYPE_CONTINUOUS)
	gain:SetCode(EVENT_PHASE+PHASE_STANDBY)
	gain:SetRange(LOCATION_FZONE)
	gain:SetCountLimit(1)
	gain:SetOperation(function(e)
		local world=e:GetHandler()
		if world:GetCounter(SPC)<12 then world:AddCounter(SPC,1) end
	end)
	c:RegisterEffect(gain)
end
