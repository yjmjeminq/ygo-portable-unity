-- Speed Spell - Angel Baton (story riding duels).
local s,id=GetID()
local SPEED_WORLD=511090001
local SPC=0x0091

function s.initial_effect(c)
	local e=Effect.CreateEffect(c)
	e:SetCategory(CATEGORY_DRAW+CATEGORY_HANDES)
	e:SetType(EFFECT_TYPE_ACTIVATE)
	e:SetCode(EVENT_FREE_CHAIN)
	e:SetCost(s.cost)
	e:SetTarget(s.target)
	e:SetOperation(s.operation)
	c:RegisterEffect(e)
end

function s.cost(e,tp,eg,ep,ev,re,r,rp,chk)
	local world=Duel.GetFieldCard(tp,LOCATION_FZONE,0)
	if chk==0 then
		return world and world:IsFaceup() and world:IsCode(SPEED_WORLD)
			and world:IsCanRemoveCounter(tp,SPC,2,REASON_COST)
	end
	world:RemoveCounter(tp,SPC,2,REASON_COST)
end

function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
	if chk==0 then return Duel.IsPlayerCanDraw(tp,2) end
	Duel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,tp,2)
	Duel.SetOperationInfo(0,CATEGORY_HANDES,nil,0,tp,1)
end

function s.operation(e,tp)
	if Duel.Draw(tp,2,REASON_EFFECT)>0 then
		Duel.BreakEffect()
		Duel.DiscardHand(tp,Card.IsDiscardable,1,1,REASON_EFFECT+REASON_DISCARD)
	end
end
