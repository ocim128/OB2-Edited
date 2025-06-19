using OpenBullet2.Core.Services;
using RuriLib.Helpers;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data; 

namespace OpenBullet2.Native.ViewModels 
{
    
    public class ConfigStackerViewModel : ViewModelBase
    {
        private readonly ConfigService configService;

        public event Action<IEnumerable<BlockViewModel>> SelectionChanged;

        private readonly List<(BlockInstance, int)> deletedBlocks = new();
        private BlockViewModel lastSelectedBlock = null;

        
        private List<BlockViewModel> _originalStack;

        
        private ObservableCollection<BlockViewModel> stack;
        public ObservableCollection<BlockViewModel> Stack
        {
            get => stack;
            set
            {
                stack = value;
                OnPropertyChanged();
            }
        }

        
        
        
        


        public ConfigStackerViewModel()
        {
            configService = SP.GetService<ConfigService>();
            
        }

        public void CreateBlock(BlockDescriptor descriptor)
        {
            
            var newBlockInstance = BlockFactory.GetBlock<BlockInstance>(descriptor.Id);
            var newBlockVm = new BlockViewModel(newBlockInstance); 

            
            int insertIndex = Stack.Count; 
            if (Stack.Any(b => b != null && b.Selected)) 
            {
                
                for(int i = Stack.Count - 1; i >= 0; i--)
                {
                    if(Stack[i] != null && Stack[i].Selected)
                    {
                        insertIndex = i + 1; 
                        break;
                    }
                }
            }

            
            if (insertIndex < 0 || insertIndex > Stack.Count)
            {
                insertIndex = Stack.Count; 
            }

            Stack.Insert(insertIndex, newBlockVm);

            
            if (_originalStack != null)
            {
                 
                 
                 
                 if(insertIndex >= 0 && insertIndex <= _originalStack.Count)
                 {
                    _originalStack.Insert(insertIndex, newBlockVm);
                 } else {
                    _originalStack.Add(newBlockVm); 
                 }
            }


            SelectBlock(newBlockVm, false);
            SaveStack(); 
            
            ApplySearchFilter(null); 
        }

        public void SelectBlock(BlockViewModel block, bool ctrl = false, bool shift = false)
        {
            
            
            
            if (Stack != null && lastSelectedBlock != null && !Stack.Contains(lastSelectedBlock)) 
            {
                lastSelectedBlock = null;
            }

            if (ctrl)
            {
                
                
                if (block != null) 
                {
                    block.Selected = !block.Selected; 
                    lastSelectedBlock = block.Selected ? block : null;
                }
            }
            else if (shift)
            {
                
                
                 if (lastSelectedBlock == null || lastSelectedBlock == block)
                {
                    if (block != null) 
                    {
                        block.Selected = true; 
                    }
                    lastSelectedBlock = block;
                }
                
                
                else
                {
                     
                    if (Stack != null) 
                    {
                        foreach (var b in Stack)
                        {
                            if (b != null) 
                            {
                                b.Selected = false; 
                            }
                        }
                    }


                    
                    if (Stack != null) 
                    {
                        
                        var lastSelectedBlockIndex = (lastSelectedBlock != null) ? Stack.IndexOf(lastSelectedBlock) : -1; 
                        var itemIndex = (block != null) ? Stack.IndexOf(block) : -1; 

                        
                        if(lastSelectedBlockIndex != -1 && itemIndex != -1)
                        {
                            var minIndex = Math.Min(lastSelectedBlockIndex, itemIndex);
                            var maxIndex = Math.Max(lastSelectedBlockIndex, itemIndex);

                            
                            for (var i = minIndex; i <= maxIndex; i++)
                            {
                                
                                if (i >= 0 && i < Stack.Count) 
                                {
                                    var item = Stack[i]; 
                                    if (item != null) 
                                    {
                                         item.Selected = true; 
                                    }
                                }
                            }
                        }
                    }

                    lastSelectedBlock = block;
                }
            }
            else 
            {
                 
                 if (Stack != null) 
                 {
                    foreach (var b in Stack)
                    {
                        if (b != null) 
                        {
                            b.Selected = false; 
                        }
                    }
                 }

                
                if (block != null) 
                {
                    block.Selected = true; 
                }

                lastSelectedBlock = block != null && block.Selected ? block : null; 
            }

            
            
             if (Stack != null) 
             {
                SelectionChanged?.Invoke(Stack.Where(s => s != null && s.Selected)); 
             } else {
                 
                 SelectionChanged?.Invoke(Enumerable.Empty<BlockViewModel>()); 
             }
        }

        public void RemoveSelected()
        {
            if (Stack == null) return; 

            
            var selectedBlocksToRemove = Stack.Where(b => b != null && b.Selected).ToList(); 

            
            
            for (int i = Stack.Count - 1; i >= 0; i--)
            {
                
                if (i >= 0 && i < Stack.Count) 
                {
                    var block = Stack[i];
                    
                    if (block != null && selectedBlocksToRemove.Contains(block)) 
                    {
                         deletedBlocks.Add((block.Block, i)); 
                         Stack.RemoveAt(i);
                    }
                }
            }

            
            if (_originalStack != null) 
            {
                 
                for (int i = _originalStack.Count - 1; i >= 0; i--)
                {
                    
                     if (i >= 0 && i < _originalStack.Count) 
                    {
                        var originalBlockVm = _originalStack[i];
                         
                        if (originalBlockVm != null && selectedBlocksToRemove.Contains(originalBlockVm)) 
                        {
                             _originalStack.RemoveAt(i);
                        }
                    }
                }
            }


            SelectBlock(null, false);
            SaveStack(); 
            ApplySearchFilter(null); 
        }

        public void MoveSelectedUp()
        {
            if (Stack == null) return; 

            
            for (var i = 0; i < Stack.Count; i++)
            {
                 
                 if (i >= 0 && i < Stack.Count) 
                 {
                    var block = Stack[i];

                    
                    if (block != null && block.Selected && i > 0) 
                    {
                        Stack.Move(i, i - 1); 
                    }
                 }
            }
             
             
             if (_originalStack != null && Stack != null) 
            {
                 _originalStack = new List<BlockViewModel>(Stack.Where(b => b != null)); 
            }


            SaveStack(); 
            
        }

        public void MoveSelectedDown()
        {
             if (Stack == null) return; 

            
            for (var i = Stack.Count - 1; i >= 0; i--)
            {
                 
                 if (i >= 0 && i < Stack.Count) 
                 {
                    var block = Stack[i];

                     
                    if (block != null && block.Selected && i < Stack.Count - 1) 
                    {
                        Stack.Move(i, i + 1); 
                    }
                 }
            }
            
            
             if (_originalStack != null && Stack != null) 
            {
                 _originalStack = new List<BlockViewModel>(Stack.Where(b => b != null)); 
            }

            SaveStack(); 
            
        }

        public void CloneSelected()
        {
             if (Stack == null) return; 

            
            var selected = Stack.Where(b => b != null && b.Selected).ToList(); 

            foreach (var blockVm in selected) 
            {
                
                if (blockVm != null && blockVm.Block != null) 
                {
                     var newBlockInstance = Cloner.Clone<BlockInstance>(blockVm.Block); 
                     var newBlockVm = new BlockViewModel(newBlockInstance); 

                     var insertIndex = Stack.IndexOf(blockVm) + 1;
                     
                     if (insertIndex >= 0 && insertIndex <= Stack.Count)
                     {
                        Stack.Insert(insertIndex, newBlockVm);
                        
                        if (_originalStack != null) 
                        {
                             
                            var originalIndex = -1;
                             
                             for(int i = 0; i < _originalStack.Count; i++)
                             {
                                 if (_originalStack[i] != null && _originalStack[i].Block == blockVm.Block)
                                 {
                                     originalIndex = i;
                                     break;
                                 }
                             }

                            if (originalIndex != -1)
                            {
                                
                                 var originalInsertIndex = originalIndex + 1;
                                if(originalInsertIndex >= 0 && originalInsertIndex <= _originalStack.Count)
                                {
                                    _originalStack.Insert(originalInsertIndex, newBlockVm);
                                } else {
                                    _originalStack.Add(newBlockVm); 
                                }
                            } else {
                                 _originalStack.Add(newBlockVm); 
                            }
                        }
                     } else {
                         
                         Stack.Add(newBlockVm);
                          if (_originalStack != null) _originalStack.Add(newBlockVm); 
                     }
                } else {
                     
                     Console.WriteLine("Attempted to clone a null BlockViewModel or one with a null BlockInstance!");
                }
            }

            SaveStack(); 
            ApplySearchFilter(null); 
        }

        public void EnableDisableSelected()
        {
             if (Stack == null) return; 

             foreach (var blockVm in Stack.Where(b => b != null && b.Selected)) 
            {
                 
                 if (blockVm != null && blockVm.Block != null) 
                 {
                    blockVm.Block.Disabled = !blockVm.Block.Disabled; 
                    blockVm.Disabled = blockVm.Block.Disabled; 

                    
                    if (_originalStack != null) 
                    {
                         var originalBlockVm = _originalStack.FirstOrDefault(b => b != null && b.Block == blockVm.Block); 
                         if(originalBlockVm != null && originalBlockVm.Block != null) 
                         {
                              originalBlockVm.Block.Disabled = blockVm.Block.Disabled; 
                              originalBlockVm.Disabled = blockVm.Block.Disabled; 
                         }
                    }
                 } else {
                      
                     Console.WriteLine("Attempted to enable/disable a null BlockViewModel or one with a null BlockInstance!");
                 }
            }
             
            
        }

        public void Undo()
        {
            if (deletedBlocks.Count == 0)
            {
                return;
            }

            var toRestore = deletedBlocks.Last();
            deletedBlocks.Remove(toRestore);

            
            if (toRestore.Item1 != null) 
            {
                var restoredBlockVm = new BlockViewModel(toRestore.Item1); 

                
                if (Stack != null && toRestore.Item2 >= 0 && toRestore.Item2 <= Stack.Count) 
                {
                     Stack.Insert(toRestore.Item2, restoredBlockVm);
                } else if (Stack != null) { 
                     Stack.Add(restoredBlockVm);
                } else {
                     
                    Stack = new ObservableCollection<BlockViewModel>{ restoredBlockVm };
                }


                
                if (_originalStack != null) 
                {
                     
                     var originalInsertIndex = toRestore.Item2; 
                     if (originalInsertIndex >= 0 && originalInsertIndex <= _originalStack.Count)
                     {
                        _originalStack.Insert(originalInsertIndex, restoredBlockVm);
                     } else {
                        _originalStack.Add(restoredBlockVm); 
                     }
                } else {
                     
                    _originalStack = new List<BlockViewModel>{ restoredBlockVm };
                }
            } else {
                
                Console.WriteLine("Attempted to undo a deleted block with a null BlockInstance!");
            }


            SaveStack(); 
            ApplySearchFilter(null); 
        }

        
        public override void UpdateViewModel()
        {
            
            if (configService?.SelectedConfig?.Stack == null) 
            {
                 
                 _originalStack = new List<BlockViewModel>();
                 Stack = new ObservableCollection<BlockViewModel>();
                 base.UpdateViewModel(); 
                 return; 
            }

            
            
            
            _originalStack = configService.SelectedConfig.Stack
                .Where(b => b != null) 
                .Select(b => new BlockViewModel(b)) 
                .ToList();

            
            
            
            Stack = new ObservableCollection<BlockViewModel>(_originalStack.Where(b => b != null)); 

            
            if (Stack.Count == 0)
            {
                SelectBlock(null, false); 
            }


            base.UpdateViewModel(); 
        }
        


        private void SaveStack()
        {
             
             
             
             if (Stack != null) 
             {
                
                _originalStack = new List<BlockViewModel>(Stack.Where(b => b != null)); 

                
                
                configService.SelectedConfig.Stack = Stack
                    .Where(b => b != null && b.Block != null) 
                    .Select(b => b.Block) 
                    .ToList(); 

             } else {
                 
                 configService.SelectedConfig.Stack = new List<BlockInstance>(); 
                 _originalStack = new List<BlockViewModel>(); 
             }
        }

        
        public void ApplySearchFilter(string searchText)
        {
             
             if (Stack == null) Stack = new ObservableCollection<BlockViewModel>(); 

            
            if (_originalStack == null) 
            {
                
                
                _originalStack = new List<BlockViewModel>(Stack.Where(b => b != null)); 
            }


            
            Stack.Clear();

            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                
                if (_originalStack != null) 
                {
                     
                     foreach (var block in _originalStack.Where(b => b != null)) 
                    {
                         
                        Stack.Add(block);
                    }
                }
            }
            else
            {
                
                var lowerSearchText = searchText.ToLowerInvariant();

                
                 if (_originalStack != null) 
                {
                     
                     foreach (var block in _originalStack.Where(b => b != null)) 
                    {
                         
                         
                        var matchesLabel = block.Label.ToLowerInvariant().Contains(lowerSearchText); 
                        var matchesType = block.Block?.Descriptor?.Name != null && block.Block.Descriptor.Name.ToLowerInvariant().Contains(lowerSearchText);

                        if (matchesLabel || matchesType)
                        {
                            Stack.Add(block);
                        }
                    }
                }
            }
        }
        

    }

    
    
    public class BlockViewModel : ViewModelBase 
    {
        
        
        
        public BlockInstance Block { get; init; }

        
        private bool selected = false;
        public bool Selected
{
    get => selected;
    set
    {
        selected = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(ForegroundColor));
    }
}

        
        public string Label 
        {
            
            
            
            get => Block?.Label ?? string.Empty;

            set
            {
                
                
                if (Block != null)
                {
                    Block.Label = value;
                    OnPropertyChanged(); 
                }
            }
        }

        
       public bool Disabled 
{
    get => Block?.Disabled ?? false;
    set
    {
        if (Block != null)
        {
            Block.Disabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(ForegroundColor));
        }
    }
}

        
        
      public string BackgroundColor 
{
    get
    {
        if (Disabled)
        {
            return "#BDBDBD"; // Medium gray for disabled blocks
        }
        else if (Selected)
        {
            return "#BBDEFB"; // Light blue for selected blocks
        }
        else
        {
            return (Block?.Descriptor?.Category != null) ? Block.Descriptor.Category.BackgroundColor : "#E3F2FD"; // Light blue default
        }
    }
}
        
        
       public string ForegroundColor 
{
    get
    {
        if (Selected)
        {
            return "#0D47A1"; // Dark blue for selected blocks
        }
        else if (Disabled)
        {
            return "#FFFFFF"; // White for disabled blocks
        }
        else
        {
            return (Block?.Descriptor?.Category != null) ? Block.Descriptor.Category.ForegroundColor : "#0D47A1"; // Dark blue default
        }
    }
}
        
        public BlockViewModel(BlockInstance block)
        {
            
            Block = block;
            
            
            
        }
    }
}
