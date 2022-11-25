﻿using ReviaRace.Genes;
using ReviaRace.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace ReviaRace.Comps
{
    public class MapSoulReaperComp : MapComponent
    {
        public MapSoulReaperComp(Map map) : base(map)
        {

        }
        IList<ReviaComp> revias;
        int pawnCount = -1;
        bool ShouldRefreshList()
        {
            if (map == null) return false;
            if(ReviaTailGene.flag)
            {
                ReviaTailGene.flag = false;
                return true;
            }
            return pawnCount != map.mapPawns.AllPawnsCount;
        }
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (ShouldRefreshList())
            {
                var def = GenDefDatabase.GetDef(typeof(GeneDef), "ReviaTail") as GeneDef;
                revias = map.mapPawns.AllPawns.Where(x => x.IsRevia()).Select(x=>new ReviaComp(x)).ToList();
                pawnCount = map.mapPawns.AllPawnsCount;
                

            }
            foreach (var revia in revias)
                revia.CompTick();
          
           

        }
        
    }
}