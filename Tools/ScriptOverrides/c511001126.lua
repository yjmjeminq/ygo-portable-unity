-- Card of Sanctity (anime)
local s,id=GetID()
function s.initial_effect(c)
	local e=Effect.CreateEffect(c)
	e:SetDescription(aux.Stringid(id,0))
	e:SetCategory(CATEGORY_DRAW)
	e:SetType(EFFECT_TYPE_ACTIVATE)
	e:SetCode(EVENT_FREE_CHAIN)
	e:SetTarget(s.target)
	e:SetOperation(s.operation)
	c:RegisterEffect(e)
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
	local own=math.max(0,6-Duel.GetMatchingGroupCount(nil,tp,LOCATION_HAND,0,e:GetHandler()))
	local other=math.max(0,6-Duel.GetFieldGroupCount(tp,0,LOCATION_HAND))
	if chk==0 then
		return own+other>0 and (own==0 or Duel.IsPlayerCanDraw(tp,own))
			and (other==0 or Duel.IsPlayerCanDraw(1-tp,other))
	end
	local drawing_player=own==0 and 1-tp or other==0 and tp or PLAYER_ALL
	Duel.SetOperationInfo(0,CATEGORY_DRAW,nil,0,drawing_player,own+other)
end
function s.operation(e,tp,eg,ep,ev,re,r,rp)
	local own=6-Duel.GetFieldGroupCount(tp,LOCATION_HAND,0)
	local other=6-Duel.GetFieldGroupCount(tp,0,LOCATION_HAND)
	if own>0 then Duel.Draw(tp,own,REASON_EFFECT) end
	if other>0 then Duel.Draw(1-tp,other,REASON_EFFECT) end
end
