import React, { useState, useEffect, useRef, useCallback } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Sun, Moon, Search, ArrowUp } from 'lucide-react';
import { Button } from './components/ui/button';
import WikipediaImporter from './components/WikipediaImporter';
import debounce from 'lodash/debounce';
import axios from 'axios';

// connect to the backend API
const API_BASE_URL = 'http://localhost:5268/api';

// keep te simple API functions outside the component
const checkTermExists = async (term: string): Promise<boolean> => {
  if (!term.trim()) return false;
  try {
    const response = await axios.get(`${API_BASE_URL}/search/check?term=${encodeURIComponent(term)}`);
    return response.data.exists;
  } catch (error) {
    console.error('Error checking term:', error);
    return false;
  }
};

// fetch search suggestions (autocomplete)
const fetchSuggestions = async (q: string) => {
  try {
    console.log('Fetching suggestions for:', q); // debugging statement. we can emit it later. 
    const response = await axios.get(`${API_BASE_URL}/search/autocomplete?q=${encodeURIComponent(q)}`);
    console.log('Received word suggestions:', response.data); // debugging statement. we can emit it later. 
    return response.data;
  } catch (error) {
    console.error('Error fetching suggestions:', error);
    return [];
  }
};

// fetch search results
const fetchResults = async (q: string) => {
  if (!q.trim()) return { results: [], totalCount: 0, searchTime: 0, query: q, operation: '' };
  
  try {
    const response = await axios.get(`${API_BASE_URL}/search?q=${encodeURIComponent(q)}`);
    return response.data;
  } catch (error) {
    console.error('Error fetching results:', error);
    // return empty results on error
    return { 
      results: [], 
      totalCount: 0, 
      searchTime: 0, 
      query: q, 
      operation: 'error' 
    };
  }
};

interface Suggestion {
  id: string;
  displayText: string;
  matchCount?: number;
  isQuerySuggestion?: boolean;
  isPopular?: boolean;
  isError?: boolean;
}

interface Result {
  id: string;
  title: string;
  snippet: string;
  url?: string;
  score?: number;
}

interface Document {
  title: string;
  content: string;
}

interface SearchResponse {
  results: Result[];
  totalCount: number;
  searchTime: number;
  query: string;
  operation: string;
}

export default function SearchEngineGUI() {
  const [query, setQuery] = useState('');
  const [exists, setExists] = useState(false);
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [searchResponse, setSearchResponse] = useState<SearchResponse>({
    results: [],
    totalCount: 0,
    searchTime: 0,
    query: '',
    operation: ''
  });
  const [visibleCount, setVisibleCount] = useState(10);
  const [doc, setDoc] = useState<Document | null>(null);
  const [page, setPage] = useState('home');
  const [darkMode, setDarkMode] = useState(false);
  const [inputFocused, setInputFocused] = useState(false);
  const [showTopButton, setShowTopButton] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isFetchingSuggestions, setIsFetchingSuggestions] = useState(false);
  const [highlightEnabled, setHighlightEnabled] = useState(true);
  const [lastExecutedSearchType, setLastExecutedSearchType] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // check if current term exists using bloom filter when user types
  const checkExistence = useRef(
    debounce(async (term: string) => {
      if (term.trim()) {
        // only check the last word if there are multiple words
        const lastWord = term.trim().split(/\s+/).pop() || '';
        setExists(await checkTermExists(lastWord));
      } else {
        setExists(false);
      }
    }, 300)
  ).current;

  const debouncedFetch = useRef(
    debounce(async (fullQuery: string) => {
      if (!fullQuery.trim()) {
        setSuggestions([]);
        return;
      }
      
      try {
        setIsFetchingSuggestions(true);
        
        // Get only the last word for autocomplete
        const lastWord = fullQuery.trim().split(/\s+/).pop() || '';
        
        // Only fetch if we have at least 1 character
        if (lastWord.length > 0) {
          // Use the last word for the autocomplete API call
          const results = await fetchSuggestions(lastWord);
          // Only update if we still have input focus to prevent unnecessary renders
          setSuggestions(Array.isArray(results) && results.length > 0 ? results : []);
        } else {
          setSuggestions([]);
        }
      } catch (error) {
        console.error('Error in debouncedFetch:', error);
        setSuggestions([]);
      } finally {
        setIsFetchingSuggestions(false);
      }
    }, 150) 
  ).current;

  useEffect(() => {
    checkExistence(query);
    
    // Only fetch suggestions when input has content
    if (inputFocused && query.trim()) {
      debouncedFetch(query);
    }
  }, [query, checkExistence, debouncedFetch, inputFocused]);

  // Clear suggestions when input loses focus
  useEffect(() => {
    if (!inputFocused) {
      // Don't immediately clear to avoid flickering during clicks
      const timer = setTimeout(() => {
        if (!inputFocused) {
          setSuggestions([]);
        }
      }, 250);
      return () => clearTimeout(timer);
    } else if (query.trim()) {
      // When input gets focus and has content, fetch suggestions
      debouncedFetch(query);
    }
  }, [inputFocused, debouncedFetch, query]);

  // debug effect to log state changes
  useEffect(() => {
    console.log('Dropdown conditions:', { 
      suggestionsLength: suggestions.length, 
      query: !!query, 
      inputFocused 
    });
  }, [suggestions, query, inputFocused]);

  useEffect(() => {
    // check for user preference in localStorage or system preference
    const savedTheme = localStorage.getItem('theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
      setDarkMode(true);
    }
  }, []);

  useEffect(() => {
    // apply dark mode class and save preference
    document.documentElement.classList.toggle('dark', darkMode);
    localStorage.setItem('theme', darkMode ? 'dark' : 'light');
  }, [darkMode]);

  const toggleDarkMode = () => setDarkMode(prev => !prev);

  // determine what type of search is being performed for display purposes
  const getSearchType = (queryText: string) => {
    if (queryText.endsWith('*')) return 'Prefix';
    if (queryText.endsWith('#')) return 'Ranked';
    if (queryText.includes('&&') || queryText.includes('||')) return 'Boolean';
    if (queryText.includes(' ')) return 'Phrase';
    return 'Exact';
  };

  const handleSearch = async () => {
    if (!query.trim()) return;
    
    // Clear suggestions first to prevent flickering
    setSuggestions([]);
    setInputFocused(false);
    setVisibleCount(10);
    setIsLoading(true);
    
    // save the current search type before executing
    const currentSearchType = getSearchType(query);
    setLastExecutedSearchType(currentSearchType);
    
    setPage('results');
    const response = await fetchResults(query);
    setSearchResponse(response);
    setIsLoading(false);
    
    // Blur input to remove focus
    inputRef.current?.blur();
    
    if (containerRef.current) containerRef.current.scrollTop = 0;
  };

  const fetchDocument = async (id: string) => {
    try {
      // get the document title from the search results
      const result = searchResponse.results.find(r => r.id === id);
      if (!result) return { title: "Unknown", content: "Document not found" };
      
      // fetch the full document with highlighted search terms
      const response = await axios.get(
        `${API_BASE_URL}/search/document/${encodeURIComponent(result.title)}?searchTerm=${encodeURIComponent(searchResponse.query)}&operation=${encodeURIComponent(searchResponse.operation)}`
      );
      return response.data;
    } catch (error) {
      console.error('Error fetching document:', error);
      return { title: "Error", content: "Failed to load document content." };
    }
  };

  const openDoc = async (id: string) => {
    setInputFocused(false);
    setSuggestions([]);
    setIsLoading(true);
    
    // fetch document with term highlighting
    const document = await fetchDocument(id);
    setDoc(document);
    setPage('document');
    setIsLoading(false);
  };

  const onScroll = useCallback(() => {
    if (!containerRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = containerRef.current;
    setShowTopButton(scrollTop > 200);
    if (scrollTop + clientHeight >= scrollHeight - 20) {
      setVisibleCount(v => Math.min(v + 10, searchResponse.results.length));
    }
  }, [searchResponse.results.length]);

  const scrollToTop = () => {
    if (containerRef.current) {
      containerRef.current.scrollTo({ top: 0, behavior: 'smooth' });
    }
  };

  const handleSuggestionClick = (suggestion: string) => {
    // replace only the last word in the query with the suggestion
    const words = query.split(' ');
    if (words.length > 1) {
      // keep all words except the last one and then add the suggestion
      words.pop(); // remove last word
      setQuery(words.join(' ') + ' ' + suggestion);
    } else {
      // just use suggestion when empty or alone
      setQuery(suggestion);
    }
    
    // Keep focus on the input
    inputRef.current?.focus();
    
    // Clear suggestions immediately to prevent flickering
    setSuggestions([]);
  };

  return (
    <div
      ref={containerRef}
      className={`min-h-screen transition-colors duration-200 ${
        darkMode ? 'dark bg-slate-900 text-white' : 'bg-slate-50 text-slate-900'
      }`}
    >
      <div className="max-w-6xl mx-auto p-4">
        <header className="py-4 flex justify-between items-center">
          <h1 className="text-xl font-bold">Search Engine</h1>
          <div className="flex items-center space-x-2">
            <WikipediaImporter />
            <button
              onClick={toggleDarkMode}
              className="p-2 rounded-full bg-slate-200 dark:bg-slate-800"
              aria-label={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}
            >
              {darkMode ? <Sun size={20} /> : <Moon size={20} />}
            </button>
          </div>
        </header>

        <div className="flex flex-col items-center justify-center">
          <div 
            className="w-full max-w-2xl flex flex-col items-center transition-all duration-500 ease-in-out"
            style={{ 
              marginTop: page === 'home' ? '15vh' : '0.5vh'
            }}
          >
            {page === 'home' ? (
              <img
                src={darkMode ? "/Wikifinder-dark.png" : "/Wikifinder.png"}
                alt="WikiFINDER logo"
                className="max-w-md w-full mb-8 transition-opacity duration-300"
              />
            ) : (
              <img
                src={darkMode ? "/Wikifindercompact.png" : "/Wikifindercompact.png"}
                alt="WikiFINDER compact logo"
                className="max-w-[140px] w-full mb-3 transition-opacity duration-300"
              />
            )}

            <div className="relative w-full">
              <div className="flex items-center w-full bg-white bg-opacity-60 dark:bg-gray-800 dark:bg-opacity-60 backdrop-blur-md rounded-full shadow-md p-3">
                <Search className="absolute left-5 w-5 h-5 text-gray-500 dark:text-gray-300" />
                <input 
                  ref={inputRef} 
                  value={query} 
                  onFocus={() => {
                    setInputFocused(true);
                    // Rrigger suggestion fetch on focus if theres content
                    if (query.trim()) {
                      debouncedFetch(query);
                    }
                  }}
                  onBlur={() => {
                    setTimeout(() => setInputFocused(false), 500);
                  }}
                  onChange={e => {
                    setQuery(e.target.value);
                  }} 
                  onKeyDown={e => {
                    if (e.key === 'Enter') {
                      handleSearch();
                    } else if (e.key === 'Escape') {
                      setSuggestions([]);
                      inputRef.current?.blur();
                    }
                  }} 
                  placeholder="Type to search (use * for prefix, && || for boolean)" 
                  className="pl-10 pr-14 w-full bg-transparent border-none outline-none placeholder-gray-400 dark:placeholder-gray-500 text-gray-900 dark:text-gray-100"
                />  
                {query.trim() && (
                  <div className={`absolute right-20 w-3 h-3 rounded-full ${exists ? 'bg-green-400' : 'bg-red-400'}`} title={exists ? "Term exists in index" : "Term not found in index"} />
                )}
                {isFetchingSuggestions && (
                  <div className="absolute right-28 w-3 h-3">
                    <div className="animate-pulse w-full h-full rounded-full bg-blue-400" title="Loading suggestions..."></div>
                  </div>
                )}
                <Button onClick={handleSearch} aria-label="Search" className="absolute right-4 p-2 rounded-full shadow-md bg-white dark:bg-gray-700">
                  <Search className="w-5 h-5 text-gray-700 dark:text-gray-200" />
                </Button>
              </div>
              
              <AnimatePresence>
                {inputFocused && suggestions.length > 0 && (
                  <motion.ul 
                    role="listbox" 
                    initial={{ opacity: 0, y: -5 }} 
                    animate={{ opacity: 1, y: 0 }} 
                    exit={{ opacity: 0, y: -5 }} 
                    className="absolute top-full left-0 mt-2 w-full bg-white dark:bg-gray-800 rounded-xl shadow-xl overflow-auto max-h-80 z-50 border border-gray-200 dark:border-gray-700"
                  >
                    {suggestions.map((s, index) => {
                      // process the full suggestion to show
                      const words = query.split(' ');
                      const lastWordIndex = words.length - 1;
                      
                      return (
                        <li 
                          key={`${s}-${index}`} 
                          role="option" 
                          onMouseDown={() => {
                            // replace only the last word in the query with the suggestion
                            const words = query.split(' ');
                            if (words.length > 1) {
                              // keep all words except the last one and then add the suggestion
                              words.pop(); // evaporate last word
                              setQuery(words.join(' ') + ' ' + s);
                            } else {
                              // just use suggestion when empty or alone
                              setQuery(s);
                            }
                            // Keep input focused after selection
                            setTimeout(() => {
                              inputRef.current?.focus();
                            }, 10);
                          }}
                          onClick={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                          }}
                          className="px-4 py-2.5 text-gray-800 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700 cursor-pointer border-b border-gray-100 dark:border-gray-700 last:border-b-0"
                        >
                          <div className="flex items-center">
                            <span className="font-medium">
                              {(() => {
                                // get the last word from the query
                                const lastWord = query.split(' ').pop() || '';
                                
                                // show the full query with suggestion
                                if (words.length > 1) {
                                  // for multi-word queries, only the suggestion part is highlighted
                                  return (
                                    <>
                                      <span className="text-gray-600 dark:text-gray-400">
                                        {words.slice(0, lastWordIndex).join(' ') + ' '}
                                      </span>
                                      <span className="text-blue-600 dark:text-blue-400 font-semibold">
                                        {s}
                                      </span>
                                    </>
                                  );
                                } else {
                                  // for single-word queries, tne matching part is highlighted
                                  if (lastWord && s.toLowerCase().startsWith(lastWord.toLowerCase())) {
                                    return (
                                      <>
                                        <span className="text-blue-600 dark:text-blue-400  font-semibold">{s.substring(0, lastWord.length)}</span>
                                        <span>{s.substring(lastWord.length)}</span>
                                      </>
                                    );
                                  } else {
                                    return s;
                                  }
                                }
                              })()}
                            </span>
                          </div>
                        </li>
                      );
                    })}
                  </motion.ul>
                )}
              </AnimatePresence>
            </div>
          </div>

          {page === 'results' && (
            <div 
              ref={containerRef} 
              onScroll={onScroll} 
              className="mt-3 space-y-2 overflow-y-auto overflow-x-hidden max-h-[80vh] w-full opacity-100 transition-opacity duration-300"
            >
              {isLoading ? (
                <div className="flex justify-center p-8">
                  <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-gray-900 dark:border-gray-100"></div>
                </div>
              ) : (
                <>
                  {searchResponse.totalCount > 0 ? (
                    <div className="text-sm text-gray-500 dark:text-gray-400 mb-2">
                      Found {searchResponse.totalCount} {searchResponse.totalCount === 1 ? 'match' : 'matches'} for "{searchResponse.query}" using {lastExecutedSearchType || searchResponse.operation || 'Exact'} search in {searchResponse.searchTime.toFixed(2)}s.
                    </div>
                  ) : (
                    <div className="text-sm text-gray-500 dark:text-gray-400 mb-2">
                      No results found for "{searchResponse.query}" using {lastExecutedSearchType || searchResponse.operation || 'Exact'} search.
                    </div>
                  )}

                  {searchResponse.results.slice(0, visibleCount).map((r) => (
                    <motion.div 
                      key={r.id} 
                      whileHover={{ scale: 1.01 }} 
                      className="transition-transform bg-white dark:bg-gray-800 bg-opacity-70 dark:bg-opacity-70 backdrop-blur-md p-2 rounded-xl shadow-md cursor-pointer hover:shadow-lg" 
                      onClick={() => openDoc(r.id)}
                    >
                      <h3 className="text-lg font-semibold mb-1 text-gray-900 dark:text-gray-100">{r.title}</h3>
                      <p className="text-sm text-gray-700 dark:text-gray-300 leading-snug line-clamp-2 min-h-[2.5rem]" dangerouslySetInnerHTML={{ __html: r.snippet }} />
                      {r.score !== undefined && (
                        <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                          Relevance: {r.score}%
                        </div>
                      )}
                    </motion.div>
                  ))}
                </>
              )}
            </div>
          )}

          {page === 'document' && doc && (
            <div 
              className="mt-3 w-full opacity-100 transition-opacity duration-300"
            >
              <div className="shadow-xl rounded-lg bg-white dark:bg-gray-800 p-4">
                <div className="flex justify-between items-center mb-3">
                  <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">{doc.title}</h2>
                  <Button 
                    variant="outline" 
                    size="sm" 
                    onClick={() => setHighlightEnabled(!highlightEnabled)}
                  >
                    {highlightEnabled ? 'Disable Highlighting' : 'Enable Highlighting'}
                  </Button>
                </div>
                <div 
                  className="text-sm text-gray-700 dark:text-gray-300 leading-relaxed min-h-[3rem] max-h-[78vh] overflow-y-auto p-2"
                  dangerouslySetInnerHTML={{ 
                    __html: highlightEnabled ? doc.content : doc.content.replace(/<\/?mark>/g, '') 
                  }}
                />
              </div>
              <Button className="mt-2" variant="outline" size="sm" onClick={() => setPage('results')}>Back to results</Button>
            </div>
          )}
        </div>

        {showTopButton && (
          <button onClick={scrollToTop} aria-label="Back to top" className="fixed bottom-16 left-1/2 transform -translate-x-1/2 p-3 rounded-full bg-white dark:bg-gray-700 shadow-md transition-opacity">
            <ArrowUp className="w-5 h-5 text-gray-700 dark:text-gray-300" />
          </button>
        )}
      </div>
    </div>
  );
} 