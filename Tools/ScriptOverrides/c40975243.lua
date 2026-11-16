-- SPDX-License-Identifier: AGPL-3.0-or-later
-- Tri-Brigade Revolt, from Project Ignis CardScripts
-- Scripted by Naim and pyrQ
local s,id=GetID()
function s.initial_effect(c)
	local e1=Effect.CreateEffect(c)
	e1:SetDescription(aux.Stringid(id,0))
	e1:SetCategory(CATEGORY_SPECIAL_SUMMON)
	e1:SetType(EFFECT_TYPE_ACTIVATE)
	e1:SetCode(EVENT_FREE_CHAIN)
	e1:SetHintTiming(0,TIMING_END_PHASE)
	e1:SetCountLimit(1,id,EFFECT_COUNT_CODE_OATH)
	e1:SetTarget(s.target)
	e1:SetOperation(s.activate)
	c:RegisterEffect(e1)
end
s.listed_series={SET_TRI_BRIGADE}
function s.linkfilter(c)
	return c:IsLinkMonster() and c:IsSetCard(SET_TRI_BRIGADE)
end
function s.spfilter2(c,fg,minmat,maxmat)
	return s.linkfilter(c) and c:IsLinkSummonable(nil,fg,minmat,maxmat)
end
function s.rescon(sg,e,tp,mg)
	return Duel.IsExistingMatchingCard(s.spfilter2,tp,LOCATION_EXTRA,0,1,nil,sg,#sg,#sg)
end
function s.filtercheck(c,e,tp)
	return c:IsCanBeLinkMaterial() and c:IsCanBeSpecialSummoned(e,0,tp,false,false) and c:IsRace(RACES_BEAST_BWARRIOR_WINGB)
		and (c:IsLocation(LOCATION_GRAVE) or (c:IsLocation(LOCATION_REMOVED) and c:IsFaceup()))
end
function s.material_limit(tp)
	local ft=Duel.GetLocationCount(tp,LOCATION_MZONE)
	if ft<=0 then return 0 end
	if Duel.IsPlayerAffectedByEffect(tp,CARD_BLUEEYES_SPIRIT) then ft=1 end
	local highest_link=0
	local links=Duel.GetMatchingGroup(s.linkfilter,tp,LOCATION_EXTRA,0,nil)
	for lc in links:Iter() do
		highest_link=math.max(highest_link,lc:GetLink())
	end
	-- Every material contributes at least 1 to the Link Rating.
	return math.min(ft,highest_link)
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
	if chk==0 then
		local ft=s.material_limit(tp)
		if ft==0 or not Duel.IsPlayerCanSpecialSummonCount(tp,2) or Duel.GetLocationCountFromEx(tp,tp,nil,TYPE_LINK)<=0 then
			return false
		end
		local fg=Duel.GetMatchingGroup(s.filtercheck,tp,LOCATION_GRAVE|LOCATION_REMOVED,0,nil,e,tp)
		return aux.SelectUnselectGroup(fg,e,tp,1,ft,s.rescon,0)
	end
	Duel.SetOperationInfo(0,CATEGORY_SPECIAL_SUMMON,nil,1,tp,LOCATION_GRAVE|LOCATION_REMOVED|LOCATION_EXTRA)
end
function s.activate(e,tp,eg,ep,ev,re,r,rp)
	local ft=s.material_limit(tp)
	if ft==0 or not Duel.IsPlayerCanSpecialSummonCount(tp,2) then return end
	local fg=Duel.GetMatchingGroup(aux.NecroValleyFilter(s.filtercheck),tp,LOCATION_GRAVE|LOCATION_REMOVED,0,nil,e,tp)
	local g=aux.SelectUnselectGroup(fg,e,tp,1,ft,s.rescon,1,tp,HINTMSG_SPSUMMON,s.rescon,nil,false)
	if not g or #g==0 then return end
	local c=e:GetHandler()
	for tc in aux.Next(g) do
		Duel.SpecialSummonStep(tc,0,tp,tp,false,false,POS_FACEUP)
		local e1=Effect.CreateEffect(c)
		e1:SetType(EFFECT_TYPE_SINGLE)
		e1:SetCode(EFFECT_DISABLE)
		e1:SetReset(RESET_EVENT|RESETS_STANDARD)
		tc:RegisterEffect(e1)
		local e2=e1:Clone()
		e2:SetCode(EFFECT_DISABLE_EFFECT)
		tc:RegisterEffect(e2)
	end
	Duel.SpecialSummonComplete()
	local tg=Duel.GetMatchingGroup(s.spfilter2,tp,LOCATION_EXTRA,0,nil,g,#g,#g)
	if #tg>0 then
		Duel.Hint(HINT_SELECTMSG,tp,HINTMSG_SPSUMMON)
		local sg=tg:Select(tp,1,1,nil)
		local sc=sg:GetFirst()
		Duel.LinkSummon(tp,sc,nil,g,#g,#g)
	end
end
