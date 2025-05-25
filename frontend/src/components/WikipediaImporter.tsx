import React, { useState } from 'react';
import axios from 'axios';

// API base URL
const API_BASE_URL = 'http://localhost:5268/api';

const WikipediaImporter = () => {
  const [title, setTitle] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [message, setMessage] = useState<{ text: string; type: 'success' | 'error' | '' }>({ 
    text: '', 
    type: '' 
  });
  const [isOpen, setIsOpen] = useState(false);
  
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!title.trim()) {
      setMessage({ 
        text: 'Please enter a Wikipedia article title', 
        type: 'error' 
      });
      return;
    }
    
    setIsLoading(true);
    setMessage({ text: '', type: '' });
    
    try {
      const response = await axios.post(
        `${API_BASE_URL}/search/wikipedia?title=${encodeURIComponent(title)}`
      );
      
      setMessage({ 
        text: response.data.message || 'Wikipedia article added successfully', 
        type: 'success' 
      });
      setTitle('');
    } catch (error) {
      console.error('Error adding Wikipedia article:', error);
      let errorMessage = 'Failed to add Wikipedia article';
      
      if (axios.isAxiosError(error) && error.response) {
        errorMessage = error.response.data || errorMessage;
      }
      
      setMessage({ text: errorMessage, type: 'error' });
    } finally {
      setIsLoading(false);
    }
  };
  
  const toggleOpen = () => {
    setIsOpen(!isOpen);
    if (!isOpen) {
      setMessage({ text: '', type: '' });
    }
  };
  
  return (
    <div className="relative">
      <button
        onClick={toggleOpen}
        className="bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 px-3 py-2 rounded-md text-sm font-medium transition-colors"
      >
        {isOpen ? 'Cancel' : 'Add Wikipedia Article'}
      </button>
      
      {isOpen && (
        <div className="absolute right-0 mt-2 w-96 bg-white dark:bg-slate-900 rounded-md shadow-lg p-4 z-10 border border-slate-200 dark:border-slate-700">
          <h3 className="text-lg font-semibold mb-3">Import Wikipedia Article</h3>
          <form onSubmit={handleSubmit}>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Enter Wikipedia article title"
              className="w-full p-2 mb-3 border border-slate-300 dark:border-slate-600 rounded bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100"
            />
            <button
              type="submit"
              disabled={isLoading}
              className="w-full bg-blue-600 hover:bg-blue-700 text-white font-medium py-2 px-4 rounded disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isLoading ? 'Adding...' : 'Add Article'}
            </button>
          </form>
          
          {message.text && (
            <div className={`mt-3 p-2 rounded ${message.type === 'success' ? 'bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200' : 'bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200'}`}>
              {message.text}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default WikipediaImporter; 