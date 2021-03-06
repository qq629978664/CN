#region License

/*
 Copyright 2014 - 2014 Nikita Bernthaler
 autosmite.cs is part of SFXUtility.
 
 SFXUtility is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.
 
 SFXUtility is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.
 
 You should have received a copy of the GNU General Public License
 along with SFXUtility. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

namespace SFXUtility.Feature
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.Linq;
    using Class;
    using IoCContainer;
    using LeagueSharp;
    using LeagueSharp.Common;

    #endregion

    internal class AutoSmite : Base
    {
        #region Fields

        private Activators _activators;
        private string[] _bigMinionNames;
        private Obj_AI_Minion _currentMinion;
        private HeroSpell _heroSpell;
        private List<HeroSpell> _heroSpells;
        private string[] _smallMinionNames;
        private Smite _smite;

        #endregion

        #region Constructors

        public AutoSmite(IContainer container)
            : base(container)
        {
            CustomEvents.Game.OnGameLoad += OnGameLoad;
        }

        #endregion

        #region Properties

        public override bool Enabled
        {
            get
            {
                return _activators != null && _activators.Menu != null &&
                       _activators.Menu.Item(_activators.Name + "Enabled").GetValue<bool>() && Menu != null &&
                       Menu.Item(Name + "Enabled").GetValue<KeyBind>().Active &&
                       (SmiteEnabled || HeroSpellEnabled);
            }
        }

        public override string Name
        {
            get { return "自动击杀"; }
        }

        private bool HeroSpellEnabled
        {
            get
            {
                return _heroSpell != null && _heroSpell.Available &&
                       (_heroSpell.HeroName == "Nunu" && Menu.Item(Name + "SpellsNunu").GetValue<bool>() ||
                        _heroSpell.HeroName == "Olaf" && Menu.Item(Name + "SpellsOlaf").GetValue<bool>() ||
                        _heroSpell.HeroName == "Chogath" && Menu.Item(Name + "SpellsChogath").GetValue<bool>());
            }
        }

        private bool IsValidMap
        {
            get
            {
                return Utility.Map.GetMap().Type == Utility.Map.MapType.SummonersRift ||
                       Utility.Map.GetMap().Type == Utility.Map.MapType.TwistedTreeline;
            }
        }

        private bool SmiteEnabled
        {
            get { return _smite.Available && Menu.Item(Name + "SpellsSmite").GetValue<bool>(); }
        }

        #endregion

        #region Methods

        private void ActivatorsLoaded(object o)
        {
            try
            {
                if (o is Activators && (o as Activators).Menu != null)
                {
                    _activators = (o as Activators);

                    Menu = new Menu(Name, Name);

                    var spellsMenu = new Menu("法术", Name + "Spells");
                    spellsMenu.AddItem(new MenuItem(Name + "SpellsSmite", "使用击打").SetValue(true));
                    spellsMenu.AddItem(new MenuItem(Name + "SpellsNunu", "使用努努 Q").SetValue(true));
                    spellsMenu.AddItem(new MenuItem(Name + "SpellsChogath", "使用大虫子 R").SetValue(true));
                    spellsMenu.AddItem(new MenuItem(Name + "SpellsOlaf", "使用奥拉夫 E").SetValue(true));

                    var drawingMenu = new Menu("绘制", Name + "Drawing");
                    drawingMenu.AddItem(new MenuItem(Name + "DrawingUseableColor", "可用颜色").SetValue(Color.Blue));
                    drawingMenu.AddItem(
                        new MenuItem(Name + "DrawingUnusableColor", "不可用的颜色").SetValue(Color.Gray));
                    drawingMenu.AddItem(new MenuItem(Name + "DrawingDamageColor", "损害颜色").SetValue(Color.SkyBlue));
                    drawingMenu.AddItem(new MenuItem(Name + "DrawingSmiteRange", "攻击范围").SetValue(true));
                    drawingMenu.AddItem(new MenuItem(Name + "DrawingHeroSpellsRange", "英雄法术范围").SetValue(true));
                    drawingMenu.AddItem(new MenuItem(Name + "DrawingDamageTillSmite", "直到伤害").SetValue(true));

                    Menu.AddSubMenu(spellsMenu);
                    Menu.AddSubMenu(drawingMenu);

                    Menu.AddItem(new MenuItem(Name + "BigCamps", "大Camps").SetValue(true));
                    Menu.AddItem(new MenuItem(Name + "SmallCamps", "小Camps").SetValue(false));
 //                   Menu.AddItem(new MenuItem(Name + "PacketCasting", "封包").SetValue(false));
                    Menu.AddItem(
                        new MenuItem(Name + "Enabled", "启用").SetValue(new KeyBind('N', KeyBindType.Toggle, true)));

                    _activators.Menu.AddSubMenu(Menu);

                    if (IsValidMap)
                    {
                        Utility.Map.MapType map = Utility.Map.GetMap().Type;

                        if (map == Utility.Map.MapType.SummonersRift)
                        {
                            _bigMinionNames = new[] {"SRU_Blue", "SRU_Red", "SRU_Baron", "SRU_Dragon"};
                            _smallMinionNames = new[] {"SRU_Razorbeak", "SRU_Krug", "SRU_Murkwolf", "SRU_Gromp", "SRU_Crab"};
                        }
                        else if (map == Utility.Map.MapType.TwistedTreeline)
                        {
                            _bigMinionNames = new[] {"TT_Spiderboss"};
                            _smallMinionNames = new[] {"TT_NWraith", "TT_NGolem", "TT_NWolf"};
                        }

                        _smite = new Smite();
                        _heroSpells = new List<HeroSpell>
                        {
                            new HeroSpell("Nunu", SpellSlot.Q, 155),
                            new HeroSpell("Chogath", SpellSlot.R, 175),
                            new HeroSpell("Olaf", SpellSlot.E, 325)
                        };
                        _heroSpell = _heroSpells.FirstOrDefault(s => s.Available);

                        Game.OnGameUpdate += OnGameUpdate;
                        Drawing.OnDraw += OnDraw;

                        Initialized = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteBlock(ex.Message, ex.ToString());
            }
        }

        private double CalculateDamage(Obj_AI_Minion target)
        {
            double damage = 0;
            if (HeroSpellEnabled && _heroSpell.CanUseSpell(target))
            {
                damage += _heroSpell.CalculateDamage();
            }
            if (SmiteEnabled && _smite.CanUseSpell(target))
            {
                damage += _smite.CalculateDamage();
            }
            return damage;
        }

        private void OnDraw(EventArgs args)
        {
            try
            {
                if (!Enabled)
                    return;

                var circleThickness = BaseMenu.Item("MiscCircleThickness").GetValue<Slider>().Value;

                if (SmiteEnabled && Menu.Item(Name + "DrawingSmiteRange").GetValue<bool>())
                {
                    Render.Circle.DrawCircle(ObjectManager.Player.Position, _smite.Range,
                        _smite.CanUseSpell() && _smite.IsInRange(_currentMinion)
                            ? Menu.Item(Name + "DrawingUseableColor").GetValue<Color>()
                            : Menu.Item(Name + "DrawingUnusableColor").GetValue<Color>(), circleThickness);
                }
                if (HeroSpellEnabled && Menu.Item(Name + "DrawingHeroSpellsRange").GetValue<bool>())
                {
                    Render.Circle.DrawCircle(ObjectManager.Player.Position, _heroSpell.Range + _currentMinion.BoundingRadius,
                        _heroSpell.CanUseSpell() && _heroSpell.IsInRange(_currentMinion)
                            ? Menu.Item(Name + "DrawingUseableColor").GetValue<Color>()
                            : Menu.Item(Name + "DrawingUnusableColor").GetValue<Color>(), circleThickness);
                }
                if (_currentMinion != null && _currentMinion.IsVisible && !_currentMinion.IsDead &&
                    Menu.Item(Name + "DrawingDamageTillSmite").GetValue<bool>())
                {
                    Utilities.DrawTextCentered(Drawing.WorldToScreen(_currentMinion.Position),
                        Menu.Item(Name + "DrawingDamageColor").GetValue<Color>(),
                        ((int)(_currentMinion.Health - _smite.CalculateDamage())).ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                Logger.WriteBlock(ex.Message, ex.ToString());
            }
        }

        private void OnGameLoad(EventArgs args)
        {
            try
            {
                Logger.Prefix = string.Format("{0} - {1}", BaseName, Name);

                if (IoC.IsRegistered<Activators>() && IoC.Resolve<Activators>().Initialized)
                {
                    ActivatorsLoaded(IoC.Resolve<Activators>());
                }
                else
                {
                    if (IoC.IsRegistered<Mediator>())
                    {
                        IoC.Resolve<Mediator>().Register("Activators_initialized", ActivatorsLoaded);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteBlock(ex.Message, ex.ToString());
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            try
            {
                if (!Enabled)
                    return;

                string[] names = null;
                if (Menu.Item(Name + "BigCamps").GetValue<bool>() && Menu.Item(Name + "SmallCamps").GetValue<bool>())
                {
                    names = _bigMinionNames.Concat(_smallMinionNames).ToArray();
                }
                else
                {
                    if (Menu.Item(Name + "BigCamps").GetValue<bool>())
                    {
                        names = _bigMinionNames;
                    }
                    else if (Menu.Item(Name + "SmallCamps").GetValue<bool>())
                    {
                        names = _smallMinionNames;
                    }
                }
                if (names != null)
                {
                    _currentMinion = Utilities.GetNearestMinionByNames(ObjectManager.Player.Position, names);
                }
                if (_currentMinion != null && _currentMinion.IsValid && !_currentMinion.IsDead &&
                    _currentMinion.Health <= CalculateDamage(_currentMinion))
                {
                    if (HeroSpellEnabled && _heroSpell.CanUseSpell(_currentMinion))
                    {
                        _heroSpell.CastSpell(_currentMinion);
                    }
                    if (SmiteEnabled && _smite.CanUseSpell(_currentMinion))
                    {
                        _smite.CastSpell(_currentMinion);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteBlock(ex.Message, ex.ToString());
            }
        }

        #endregion
    }
}